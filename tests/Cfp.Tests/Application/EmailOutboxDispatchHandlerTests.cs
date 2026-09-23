using Cfp.Application.Abstractions;
using Cfp.Application.Notifications;
using Cfp.Domain.Notifications;

namespace Cfp.Tests.Application;

public sealed class EmailOutboxDispatchHandlerTests
{
    [Fact]
    public async Task DispatchAsync_SendsOnlyEligibleRecipientAndIsIdempotent()
    {
        var store = new InMemoryOutboxStore(CreateReadyItem());
        var sender = new RecordingEmailSender();
        var directory = new RecordingDeliveryDirectory();
        var handler = new EmailOutboxDispatchHandler(
            store,
            new FixedRecipientPolicy(new EmailRecipientDecision(true, "speaker@example.test", null)),
            sender,
            directory);

        var job = new EmailDispatchJob("conference-1", "outbox:proposal-received:user-1");
        await handler.DispatchAsync(job, CancellationToken.None);
        await handler.DispatchAsync(job, CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Accepted, store.Item!.Status);
        Assert.Equal("provider-message-1", store.Item.ProviderMessageId);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, directory.LinkCount);
        Assert.Equal("speaker@example.test", sender.LastMessage?.RecipientAddress);
    }

    [Fact]
    public async Task DispatchAsync_SuppressesUnverifiedRecipientWithoutCallingProvider()
    {
        var store = new InMemoryOutboxStore(CreateReadyItem());
        var sender = new RecordingEmailSender();
        var handler = new EmailOutboxDispatchHandler(
            store,
            new FixedRecipientPolicy(new EmailRecipientDecision(false, null, "No confirmed address.")),
            sender,
            new RecordingDeliveryDirectory());

        await handler.DispatchAsync(
            new EmailDispatchJob("conference-1", "outbox:proposal-received:user-1"),
            CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Suppressed, store.Item!.Status);
        Assert.Equal("No confirmed address.", store.Item.StatusReason);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task DispatchAsync_UsesCampaignSenderSnapshot()
    {
        var campaignItem = CreateReadyItem() with
        {
            Category = EmailCategory.ConferenceOperations,
            TemplateId = "conference-operations",
            Subject = "Schedule update",
            PlainTextContent = "The schedule is available.",
            SenderAddress = "campaign-sender@example.test"
        };
        var store = new InMemoryOutboxStore(campaignItem);
        var sender = new RecordingEmailSender();
        var handler = new EmailOutboxDispatchHandler(
            store,
            new FixedRecipientPolicy(new EmailRecipientDecision(true, "speaker@example.test", null)),
            sender,
            new RecordingDeliveryDirectory());

        await handler.DispatchAsync(
            new EmailDispatchJob("conference-1", campaignItem.Id),
            CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Accepted, store.Item!.Status);
        Assert.Equal("campaign-sender@example.test", sender.LastMessage?.SenderAddress);
    }

    [Fact]
    public async Task DispatchAsync_MarksExpiredSendingLeaseUnknownWithoutResending()
    {
        var now = DateTimeOffset.UtcNow;
        var sending = CreateReadyItem() with
        {
            Status = EmailOutboxStatus.Sending,
            AttemptId = "attempt-old",
            LeaseExpiresAtUtc = now.AddMinutes(-1)
        };
        var store = new InMemoryOutboxStore(sending);
        var sender = new RecordingEmailSender();
        var handler = new EmailOutboxDispatchHandler(
            store,
            new FixedRecipientPolicy(new EmailRecipientDecision(true, "speaker@example.test", null)),
            sender,
            new RecordingDeliveryDirectory());

        await handler.DispatchAsync(
            new EmailDispatchJob("conference-1", "outbox:proposal-received:user-1"),
            CancellationToken.None);

        Assert.Equal(EmailOutboxStatus.Unknown, store.Item!.Status);
        Assert.Equal(0, sender.SendCount);
    }

    private static EmailOutboxItem CreateReadyItem()
    {
        var now = DateTimeOffset.UtcNow;
        return new EmailOutboxItem(
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

    private sealed class InMemoryOutboxStore(EmailOutboxItem item) : IEmailOutboxStore
    {
        private string _etag = "\"1\"";
        public EmailOutboxItem? Item { get; private set; } = item;

        public Task<Versioned<EmailOutboxItem>?> GetAsync(
            string conferenceId,
            string outboxId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Versioned<EmailOutboxItem>?>(
                Item is not null && Item.ConferenceId == conferenceId && Item.Id == outboxId
                    ? new Versioned<EmailOutboxItem>(Item, _etag)
                    : null);

        public Task<Versioned<EmailOutboxItem>> SaveAsync(
            EmailOutboxItem value,
            string expectedEtag,
            CancellationToken cancellationToken)
        {
            if (expectedEtag != _etag)
            {
                throw new RequestConflictException("stale");
            }

            Item = value;
            _etag = $"\"{int.Parse(_etag.Trim('"')) + 1}\"";
            return Task.FromResult(new Versioned<EmailOutboxItem>(value, _etag));
        }

        public Task<IReadOnlyList<Versioned<EmailOutboxItem>>> ListUnresolvedAsync(
            DateTimeOffset expiredSendingBeforeUtc,
            DateTimeOffset unreportedAcceptedBeforeUtc,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Versioned<EmailOutboxItem>>>([]);

        public Task<EmailOutboxPage> ListByConferenceAsync(
            string conferenceId,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailOutboxPage([], null));
    }

    private sealed class FixedRecipientPolicy(EmailRecipientDecision decision) : IEmailRecipientPolicy
    {
        public Task<EmailRecipientDecision> ResolveAsync(
            string userId,
            EmailCategory category,
            CancellationToken cancellationToken) =>
            Task.FromResult(decision);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public int SendCount { get; private set; }
        public EmailMessageEnvelope? LastMessage { get; private set; }

        public Task<string> SendAsync(EmailMessageEnvelope message, CancellationToken cancellationToken)
        {
            SendCount++;
            LastMessage = message;
            return Task.FromResult("provider-message-1");
        }
    }

    private sealed class RecordingDeliveryDirectory : IEmailDeliveryDirectory
    {
        public int LinkCount { get; private set; }

        public Task LinkAsync(
            string providerMessageId,
            string conferenceId,
            string outboxId,
            CancellationToken cancellationToken)
        {
            LinkCount++;
            return Task.CompletedTask;
        }

        public Task<EmailDeliveryLink?> ResolveAsync(string providerMessageId, CancellationToken cancellationToken) =>
            Task.FromResult<EmailDeliveryLink?>(null);

        public Task<bool> RecordEventReceivedAsync(
            string providerMessageId,
            string eventId,
            string eventStatus,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task MarkEventAppliedAsync(
            string providerMessageId,
            string eventId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkEventAnomalyAsync(
            string providerMessageId,
            string eventId,
            string reason,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
