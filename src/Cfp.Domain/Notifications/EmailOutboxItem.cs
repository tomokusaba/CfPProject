namespace Cfp.Domain.Notifications;

public enum EmailCategory
{
    Transactional,
    ConferenceOperations
}

public enum EmailOutboxStatus
{
    Pending,
    Ready,
    Sending,
    Accepted,
    Delivered,
    Bounced,
    Suppressed,
    FilteredSpam,
    Quarantined,
    Failed,
    Unknown,
    Cancelled
}

public sealed record EmailOutboxItem(
    string Id,
    string ConferenceId,
    string RecipientUserId,
    EmailCategory Category,
    string TemplateId,
    EmailOutboxStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ProviderMessageId,
    string? AttemptId,
    DateTimeOffset? LeaseExpiresAtUtc,
    string? StatusReason,
    string? Subject = null,
    string? PlainTextContent = null,
    string? SenderAddress = null)
{
    public EmailOutboxItem BeginSending(string attemptId, DateTimeOffset nowUtc, DateTimeOffset leaseExpiresAtUtc)
    {
        if (Status != EmailOutboxStatus.Ready)
        {
            throw new InvalidOperationException("Only ready email outbox items can be sent.");
        }

        return this with
        {
            Status = EmailOutboxStatus.Sending,
            AttemptId = attemptId,
            LeaseExpiresAtUtc = leaseExpiresAtUtc.ToUniversalTime(),
            UpdatedAtUtc = nowUtc.ToUniversalTime(),
            StatusReason = null
        };
    }

    public EmailOutboxItem MarkAccepted(string providerMessageId, DateTimeOffset nowUtc)
    {
        if (Status != EmailOutboxStatus.Sending || string.IsNullOrWhiteSpace(providerMessageId))
        {
            throw new InvalidOperationException("An in-progress send and provider message ID are required.");
        }

        return this with
        {
            Status = EmailOutboxStatus.Accepted,
            ProviderMessageId = providerMessageId,
            LeaseExpiresAtUtc = null,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public EmailOutboxItem MarkSuppressed(string reason, DateTimeOffset nowUtc)
    {
        if (Status != EmailOutboxStatus.Ready)
        {
            throw new InvalidOperationException("Only a ready outbox item can be suppressed before sending.");
        }

        return this with
        {
            Status = EmailOutboxStatus.Suppressed,
            StatusReason = reason,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public EmailOutboxItem MarkFailed(string reason, DateTimeOffset nowUtc)
    {
        if (Status is not (EmailOutboxStatus.Ready or EmailOutboxStatus.Sending))
        {
            throw new InvalidOperationException("Only a ready or in-progress outbox item can be failed.");
        }

        return this with
        {
            Status = EmailOutboxStatus.Failed,
            LeaseExpiresAtUtc = null,
            StatusReason = reason,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public EmailOutboxItem MarkUnknown(string reason, DateTimeOffset nowUtc)
    {
        if (Status is not EmailOutboxStatus.Sending and not EmailOutboxStatus.Accepted)
        {
            throw new InvalidOperationException("Only an in-progress or accepted send can become unknown.");
        }

        return this with
        {
            Status = EmailOutboxStatus.Unknown,
            LeaseExpiresAtUtc = null,
            StatusReason = reason,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public EmailOutboxItem ApplyDeliveryStatus(EmailOutboxStatus status, DateTimeOffset nowUtc)
    {
        if (status is not (EmailOutboxStatus.Delivered or
                           EmailOutboxStatus.Bounced or
                           EmailOutboxStatus.Suppressed or
                           EmailOutboxStatus.FilteredSpam or
                           EmailOutboxStatus.Quarantined or
                           EmailOutboxStatus.Failed))
        {
            throw new ArgumentException("The status is not a delivery outcome.", nameof(status));
        }

        if (Status is not (EmailOutboxStatus.Sending or EmailOutboxStatus.Accepted or EmailOutboxStatus.Unknown))
        {
            if (Status == status)
            {
                return this;
            }

            throw new InvalidOperationException("A terminal email status cannot be overwritten.");
        }

        return this with
        {
            Status = status,
            LeaseExpiresAtUtc = null,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
