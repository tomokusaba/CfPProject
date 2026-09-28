using Cfp.Domain.Notifications;

namespace Cfp.Tests.Domain;

public sealed class EmailOutboxItemTests
{
    [Fact]
    public void BeginSending_RequiresReadyAndRecordsAttemptLease()
    {
        var now = DateTimeOffset.UtcNow;
        var item = CreateReadyItem(now);

        var sending = item.BeginSending("attempt-1", now, now.AddMinutes(2));

        Assert.Equal(EmailOutboxStatus.Sending, sending.Status);
        Assert.Equal("attempt-1", sending.AttemptId);
        Assert.Equal(now.AddMinutes(2), sending.LeaseExpiresAtUtc);
        Assert.Throws<InvalidOperationException>(() => sending.BeginSending("attempt-2", now, now.AddMinutes(3)));
    }

    [Fact]
    public void DeliveryStatus_CanCompleteUnknownButCannotOverwriteTerminalState()
    {
        var now = DateTimeOffset.UtcNow;
        var unknown = CreateReadyItem(now)
            .BeginSending("attempt-1", now, now.AddMinutes(2))
            .MarkUnknown("Provider result uncertain.", now);

        var delivered = unknown.ApplyDeliveryStatus(EmailOutboxStatus.Delivered, now.AddMinutes(1));

        Assert.Equal(EmailOutboxStatus.Delivered, delivered.Status);
        Assert.Throws<InvalidOperationException>(() =>
            delivered.ApplyDeliveryStatus(EmailOutboxStatus.Bounced, now.AddMinutes(2)));
    }

    private static EmailOutboxItem CreateReadyItem(DateTimeOffset now) => new(
        "outbox:proposal-received:user-1",
        "conference-1",
        "user-1",
        EmailCategory.Transactional,
        "proposal-received",
        EmailOutboxStatus.Ready,
        now,
        now,
        null,
        null,
        null,
        null);
}
