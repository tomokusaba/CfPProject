using System.Text.Json;
using Cfp.Application.Abstractions;
using Cfp.Application.Notifications;
using Microsoft.Azure.Functions.Worker;

namespace Cfp.Functions.Triggers;

public sealed class EmailDispatchFunction(EmailOutboxDispatchHandler dispatcher)
{
    [Function(nameof(EmailDispatchFunction))]
    public async Task Run(
        [QueueTrigger("email-jobs", Connection = "AzureWebJobsStorage")]
        string message,
        CancellationToken cancellationToken)
    {
        if (message.Length > 4096)
        {
            throw new JsonException("Email queue message exceeds the allowed size.");
        }

        var job = JsonSerializer.Deserialize<EmailDispatchJob>(
            message,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (job is null ||
            string.IsNullOrWhiteSpace(job.ConferenceId) ||
            string.IsNullOrWhiteSpace(job.OutboxId))
        {
            throw new JsonException("Email queue message does not contain the required identifiers.");
        }

        await dispatcher.DispatchAsync(job, cancellationToken);
    }
}
