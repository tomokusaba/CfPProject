using Cfp.Application.Abstractions;
using Cfp.Domain.Notifications;

namespace Cfp.Application.Notifications;

public sealed class ProfileEmailRecipientPolicy(IUserProfileStore profiles) : IEmailRecipientPolicy
{
    public async Task<EmailRecipientDecision> ResolveAsync(
        string userId,
        EmailCategory category,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetAsync(userId, cancellationToken);
        if (profile is null ||
            !profile.Value.EmailVerified ||
            string.IsNullOrWhiteSpace(profile.Value.Email))
        {
            return new EmailRecipientDecision(false, null, "No confirmed email address is available.");
        }

        if (profile.Value.EmailSuppressed)
        {
            return new EmailRecipientDecision(false, null, "The recipient is suppressed from email delivery.");
        }

        if (category == EmailCategory.ConferenceOperations &&
            !profile.Value.ConferenceOperationsOptIn)
        {
            return new EmailRecipientDecision(false, null, "The recipient has not opted in to conference operations email.");
        }

        return new EmailRecipientDecision(true, profile.Value.Email, null);
    }
}

public sealed class EmailOutboxDispatchHandler(
    IEmailOutboxStore outboxStore,
    IEmailRecipientPolicy recipientPolicy,
    IEmailSender emailSender,
    IEmailDeliveryDirectory deliveryDirectory)
{
    private static readonly TimeSpan SendLease = TimeSpan.FromMinutes(2);

    public async Task DispatchAsync(EmailDispatchJob job, CancellationToken cancellationToken)
    {
        var current = await outboxStore.GetAsync(job.ConferenceId, job.OutboxId, cancellationToken);
        if (current is null)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (current.Value.Status == EmailOutboxStatus.Sending)
        {
            if (current.Value.LeaseExpiresAtUtc is { } leaseExpiry && leaseExpiry <= nowUtc)
            {
                var unknown = current.Value.MarkUnknown(
                    "The send worker stopped before confirming the provider result.",
                    nowUtc);
                await outboxStore.SaveAsync(unknown, current.ETag, cancellationToken);
            }

            return;
        }

        if (current.Value.Status != EmailOutboxStatus.Ready)
        {
            return;
        }

        var recipient = await recipientPolicy.ResolveAsync(
            current.Value.RecipientUserId,
            current.Value.Category,
            cancellationToken);
        if (!recipient.IsAllowed || string.IsNullOrWhiteSpace(recipient.EmailAddress))
        {
            var suppressed = current.Value.MarkSuppressed(
                recipient.SuppressionReason ?? "Recipient is not eligible for this communication.",
                nowUtc);
            await SaveIfCurrentAsync(suppressed, current.ETag, cancellationToken);
            return;
        }

        var sending = current.Value.BeginSending(
            Guid.NewGuid().ToString("N"),
            nowUtc,
            nowUtc.Add(SendLease));
        Versioned<EmailOutboxItem> sendingVersion;
        try
        {
            sendingVersion = await outboxStore.SaveAsync(sending, current.ETag, cancellationToken);
        }
        catch (RequestConflictException)
        {
            return;
        }

        var message = CreateMessage(sending, recipient.EmailAddress);
        string providerMessageId;
        try
        {
            providerMessageId = await emailSender.SendAsync(message, cancellationToken);
        }
        catch (EmailSendFailureException exception)
        {
            var failed = exception.OutcomeUnknown
                ? sendingVersion.Value.MarkUnknown(exception.ErrorCode ?? "Provider result is unknown.", DateTimeOffset.UtcNow)
                : sendingVersion.Value.MarkFailed(exception.ErrorCode ?? "Email provider rejected the request.", DateTimeOffset.UtcNow);
            await SaveIfCurrentAsync(failed, sendingVersion.ETag, cancellationToken);
            return;
        }

        try
        {
            await deliveryDirectory.LinkAsync(
                providerMessageId,
                sending.ConferenceId,
                sending.Id,
                cancellationToken);
            var accepted = sendingVersion.Value.MarkAccepted(providerMessageId, DateTimeOffset.UtcNow);
            await SaveIfCurrentAsync(accepted, sendingVersion.ETag, cancellationToken);
        }
        catch (RequestConflictException)
        {
            var latest = await outboxStore.GetAsync(job.ConferenceId, job.OutboxId, cancellationToken);
            if (latest?.Value.Status == EmailOutboxStatus.Sending &&
                latest.Value.LeaseExpiresAtUtc is { } leaseExpiry &&
                leaseExpiry <= DateTimeOffset.UtcNow)
            {
                await SaveIfCurrentAsync(
                    latest.Value.MarkUnknown("Provider accepted the send but persistence was interrupted.", DateTimeOffset.UtcNow),
                    latest.ETag,
                    cancellationToken);
            }
        }
    }

    private async Task SaveIfCurrentAsync(
        EmailOutboxItem item,
        string etag,
        CancellationToken cancellationToken)
    {
        try
        {
            await outboxStore.SaveAsync(item, etag, cancellationToken);
        }
        catch (RequestConflictException)
        {
            // A delivery report can legitimately race the provider-acceptance update.
        }
    }

    private static EmailMessageEnvelope CreateMessage(EmailOutboxItem item, string recipient)
    {
        var (subject, body) = item.TemplateId switch
        {
            "proposal-received" => (
                "プロポーザルを受け付けました",
                "応募を受け付けました。カンファレンスの応募ページで状態をご確認ください。"),
            "proposal-accepted" => (
                "プロポーザルの審査結果が更新されました",
                "プロポーザルの審査結果が更新されました。ログインして応募ページをご確認ください。"),
            "proposal-rejected" => (
                "プロポーザルの審査結果が更新されました",
                "プロポーザルの審査結果が更新されました。ログインして応募ページをご確認ください。"),
            "conference-operations" when
                !string.IsNullOrWhiteSpace(item.Subject) &&
                !string.IsNullOrWhiteSpace(item.PlainTextContent) =>
                (item.Subject, item.PlainTextContent),
            _ => throw new EmailSendFailureException("Email template is not configured.", outcomeUnknown: false)
        };

        return new EmailMessageEnvelope(recipient, subject, body, item.SenderAddress);
    }
}

public sealed class EmailDeliveryReportHandler(
    IEmailDeliveryDirectory deliveryDirectory,
    IEmailOutboxStore outboxStore,
    IUserProfileStore profiles)
{
    public async Task HandleAsync(
        string eventId,
        string providerMessageId,
        string providerStatus,
        CancellationToken cancellationToken)
    {
        var link = await deliveryDirectory.ResolveAsync(providerMessageId, cancellationToken)
            ?? throw new InvalidOperationException("Email delivery report references an unknown provider message.");
        var recorded = await deliveryDirectory.RecordEventReceivedAsync(
            providerMessageId,
            eventId,
            providerStatus,
            cancellationToken);
        if (!recorded)
        {
            return;
        }

        var outbox = await outboxStore.GetAsync(link.ConferenceId, link.OutboxId, cancellationToken)
            ?? throw new InvalidOperationException("Email delivery report references a missing outbox item.");
        if (!TryMapStatus(providerStatus, out var status))
        {
            if (providerStatus == "Expanded")
            {
                await deliveryDirectory.MarkEventAppliedAsync(providerMessageId, eventId, cancellationToken);
            }
            else
            {
                await deliveryDirectory.MarkEventAnomalyAsync(
                    providerMessageId,
                    eventId,
                    "Unsupported ACS delivery status.",
                    cancellationToken);
            }

            return;
        }

        try
        {
            var updated = outbox.Value.ApplyDeliveryStatus(status, DateTimeOffset.UtcNow);
            await outboxStore.SaveAsync(updated, outbox.ETag, cancellationToken);
        }
        catch (RequestConflictException)
        {
            var latest = await outboxStore.GetAsync(link.ConferenceId, link.OutboxId, cancellationToken);
            if (latest is null)
            {
                throw new InvalidOperationException("Email outbox item disappeared while applying a delivery report.");
            }

            if (latest.Value.Status is EmailOutboxStatus.Sending or EmailOutboxStatus.Accepted or EmailOutboxStatus.Unknown)
            {
                var updated = latest.Value.ApplyDeliveryStatus(status, DateTimeOffset.UtcNow);
                await outboxStore.SaveAsync(updated, latest.ETag, cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            await deliveryDirectory.MarkEventAnomalyAsync(
                providerMessageId,
                eventId,
                "Delivery report conflicts with the outbox terminal state.",
                cancellationToken);
            return;
        }

        if (status is EmailOutboxStatus.Bounced or EmailOutboxStatus.Suppressed)
        {
            await profiles.SetEmailSuppressedAsync(
                outbox.Value.RecipientUserId,
                suppressed: true,
                reason: $"Delivery report: {providerStatus}.",
                cancellationToken);
        }

        await deliveryDirectory.MarkEventAppliedAsync(providerMessageId, eventId, cancellationToken);
    }

    private static bool TryMapStatus(string providerStatus, out EmailOutboxStatus status)
    {
        status = providerStatus switch
        {
            "Delivered" => EmailOutboxStatus.Delivered,
            "Bounced" => EmailOutboxStatus.Bounced,
            "Suppressed" => EmailOutboxStatus.Suppressed,
            "FilteredSpam" => EmailOutboxStatus.FilteredSpam,
            "Quarantined" => EmailOutboxStatus.Quarantined,
            "Failed" => EmailOutboxStatus.Failed,
            _ => default
        };
        return providerStatus is "Delivered" or "Bounced" or "Suppressed" or "FilteredSpam" or "Quarantined" or "Failed";
    }
}
