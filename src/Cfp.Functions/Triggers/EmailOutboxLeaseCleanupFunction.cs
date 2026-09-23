using Cfp.Application.Abstractions;
using Cfp.Domain.Notifications;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cfp.Functions.Triggers;

public sealed class EmailOutboxLeaseCleanupFunction(
    IEmailOutboxStore outboxStore,
    ILogger<EmailOutboxLeaseCleanupFunction> logger)
{
    [Function(nameof(EmailOutboxLeaseCleanupFunction))]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")]
        TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var expired = await outboxStore.ListUnresolvedAsync(
            nowUtc.AddMinutes(-2),
            nowUtc.AddHours(-24),
            pageSize: 100,
            cancellationToken);
        var conflicts = 0;
        foreach (var item in expired)
        {
            try
            {
                var unknown = item.Value.MarkUnknown(
                    item.Value.Status == EmailOutboxStatus.Sending
                        ? "The send worker lease expired before the provider result was confirmed."
                        : "No delivery report was received within the reconciliation window.",
                    nowUtc);
                await outboxStore.SaveAsync(unknown, item.ETag, cancellationToken);
            }
            catch (RequestConflictException)
            {
                conflicts++;
            }
        }

        if (conflicts > 0)
        {
            logger.LogInformation(
                "Skipped {Count} email leases updated by another worker.",
                conflicts);
        }
    }
}
