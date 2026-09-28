using Cfp.Application.Auditing;
using Cfp.Domain.Notifications;

namespace Cfp.Application.Abstractions;

public sealed record EmailRecipientDecision(
    bool IsAllowed,
    string? EmailAddress,
    string? SuppressionReason);

public sealed record EmailMessageEnvelope(
    string RecipientAddress,
    string Subject,
    string PlainTextContent,
    string? SenderAddress = null);

public sealed record EmailDeliveryLink(string ConferenceId, string OutboxId);

public interface IEmailOutboxStore
{
    Task<Versioned<EmailOutboxItem>?> GetAsync(
        string conferenceId,
        string outboxId,
        CancellationToken cancellationToken);

    Task<Versioned<EmailOutboxItem>> SaveAsync(
        EmailOutboxItem item,
        string expectedEtag,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<EmailOutboxItem>>> ListUnresolvedAsync(
        DateTimeOffset expiredSendingBeforeUtc,
        DateTimeOffset unreportedAcceptedBeforeUtc,
        int pageSize,
        CancellationToken cancellationToken);

    Task<EmailOutboxPage> ListByConferenceAsync(
        string conferenceId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);
}

public interface IEmailRecipientPolicy
{
    Task<EmailRecipientDecision> ResolveAsync(
        string userId,
        EmailCategory category,
        CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task<string> SendAsync(
        EmailMessageEnvelope message,
        CancellationToken cancellationToken);
}

public interface IEmailDeliveryDirectory
{
    Task LinkAsync(
        string providerMessageId,
        string conferenceId,
        string outboxId,
        CancellationToken cancellationToken);

    Task<EmailDeliveryLink?> ResolveAsync(
        string providerMessageId,
        CancellationToken cancellationToken);

    Task<bool> RecordEventReceivedAsync(
        string providerMessageId,
        string eventId,
        string eventStatus,
        CancellationToken cancellationToken);

    Task MarkEventAppliedAsync(
        string providerMessageId,
        string eventId,
        CancellationToken cancellationToken);

    Task MarkEventAnomalyAsync(
        string providerMessageId,
        string eventId,
        string reason,
        CancellationToken cancellationToken);
}

public sealed class EmailSendFailureException(
    string message,
    bool outcomeUnknown,
    string? errorCode = null) : Exception(message)
{
    public bool OutcomeUnknown { get; } = outcomeUnknown;
    public string? ErrorCode { get; } = errorCode;
}

public sealed record EmailDispatchJob(string ConferenceId, string OutboxId);

public sealed record EmailOutboxPage(
    IReadOnlyList<Versioned<EmailOutboxItem>> Items,
    string? ContinuationToken);
