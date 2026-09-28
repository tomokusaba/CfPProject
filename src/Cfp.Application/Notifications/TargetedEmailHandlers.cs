using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.Proposals;
using Cfp.Domain.Conferences;
using Cfp.Domain.Notifications;
using Cfp.Domain.Proposals;

namespace Cfp.Application.Notifications;

public sealed record PreviewTargetedEmailCommand(
    IReadOnlyList<string> ProposalStatuses,
    string Subject,
    string PlainTextContent,
    string SenderAddress);

public sealed record EmailCampaignPreview(
    Versioned<EmailCampaign> Campaign,
    int EligibleRecipientCount,
    int ExcludedRecipientCount);

public sealed class PreviewTargetedEmailHandler(
    ConferenceAuthorizationService authorization,
    IProposalWorkflowStore proposals,
    IEmailRecipientPolicy recipientPolicy,
    IEmailCampaignStore campaigns)
{
    private const int MaximumCampaignRecipients = 50;

    public async Task<EmailCampaignPreview> HandleAsync(
        string conferenceId,
        PreviewTargetedEmailCommand command,
        Actor actor,
        string idempotencyKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);

        if (!Guid.TryParse(idempotencyKey, out var operationId))
        {
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(command.Subject) || command.Subject.Length > 200)
        {
            throw new ArgumentException("Subject is required and must be at most 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.PlainTextContent) || command.PlainTextContent.Length > 5_000)
        {
            throw new ArgumentException("Message body is required and must be at most 5,000 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.SenderAddress) || command.SenderAddress.Length > 320)
        {
            throw new ArgumentException("A configured sender address of at most 320 characters is required.");
        }

        var includedStatuses = command.ProposalStatuses
            .Distinct(StringComparer.Ordinal)
            .Select(status => Enum.TryParse<ProposalStatus>(status, ignoreCase: false, out var parsed)
                ? parsed
                : throw new ArgumentException("One or more proposal statuses are invalid."))
            .ToHashSet();
        if (includedStatuses.Count == 0 ||
            includedStatuses.Any(status => status is not (ProposalStatus.Submitted or ProposalStatus.UnderReview or ProposalStatus.Accepted or ProposalStatus.Rejected)))
        {
            throw new ArgumentException("Target statuses must be Submitted, UnderReview, Accepted, or Rejected.");
        }

        var targetUserIds = await GetTargetUsersAsync(conferenceId, includedStatuses, cancellationToken);
        var eligible = new List<string>();
        foreach (var userId in targetUserIds)
        {
            var recipient = await recipientPolicy.ResolveAsync(
                userId,
                EmailCategory.ConferenceOperations,
                cancellationToken);
            if (recipient.IsAllowed)
            {
                eligible.Add(userId);
                if (eligible.Count > MaximumCampaignRecipients)
                {
                    throw new ArgumentException(
                        $"This targeted email is limited to {MaximumCampaignRecipients} eligible recipients.");
                }
            }
        }

        var recipients = eligible.Order(StringComparer.Ordinal).ToArray();
        var targetSnapshot = targetUserIds.Order(StringComparer.Ordinal).ToArray();
        var excludedRecipientCount = targetSnapshot.Length - recipients.Length;
        var normalized = new
        {
            Statuses = includedStatuses.Select(status => status.ToString()).Order(StringComparer.Ordinal).ToArray(),
            TargetUsers = targetSnapshot,
            EligibleRecipients = recipients,
            ExcludedRecipientCount = excludedRecipientCount,
            SenderAddress = command.SenderAddress.Trim(),
            Subject = command.Subject.Trim(),
            Body = command.PlainTextContent.Trim()
        };
        var requestHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))))
            .ToLowerInvariant();
        var campaignId = "campaign_" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{conferenceId}\n{actor.UserId}\n{operationId:N}")))
            .ToLowerInvariant()[..32];
        var campaign = new EmailCampaign(
            campaignId,
            conferenceId,
            actor.UserId,
            includedStatuses.OrderBy(status => status.ToString(), StringComparer.Ordinal).ToArray(),
            recipients,
            command.SenderAddress.Trim(),
            command.Subject.Trim(),
            command.PlainTextContent.Trim(),
            excludedRecipientCount,
            requestHash,
            EmailCampaignState.Previewed,
            nowUtc.ToUniversalTime(),
            nowUtc.ToUniversalTime().AddMinutes(30),
            null,
            null,
            null);
        var auditEvent = CreateAudit(
            conferenceId,
            actor.UserId,
            "EmailCampaignPreviewed",
            campaignId,
            $"Targeted email preview created for {recipients.Length} eligible recipients.",
            nowUtc);
        var saved = await campaigns.CreatePreviewAsync(campaign, auditEvent, cancellationToken);
        return new EmailCampaignPreview(saved, recipients.Length, campaign.ExcludedRecipientCount);
    }

    private async Task<HashSet<string>> GetTargetUsersAsync(
        string conferenceId,
        HashSet<ProposalStatus> statuses,
        CancellationToken cancellationToken)
    {
        var users = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;
        var examined = 0;
        do
        {
            var page = await proposals.ListByConferenceAsync(
                conferenceId,
                50,
                continuationToken,
                cancellationToken);
            foreach (var proposal in page.Proposals.Select(item => item.Value))
            {
                examined++;
                if (examined > 5_000)
                {
                    throw new InvalidOperationException("Targeted email preview is limited to the first 5,000 conference proposals.");
                }

                if (statuses.Contains(proposal.Status))
                {
                    users.Add(proposal.OwnerUserId);
                }
            }

            continuationToken = page.ContinuationToken;
        } while (!string.IsNullOrEmpty(continuationToken));

        return users;
    }

    internal static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string targetId,
        string summary,
        DateTimeOffset nowUtc) =>
        new($"audit:{Guid.NewGuid():N}", conferenceId, actorUserId, operation, targetId, nowUtc.ToUniversalTime(), summary);
}

public sealed class SendTargetedEmailHandler(
    ConferenceAuthorizationService authorization,
    IEmailCampaignStore campaigns)
{
    public async Task<Versioned<EmailCampaign>> HandleAsync(
        string conferenceId,
        string campaignId,
        Actor actor,
        string reason,
        string confirmationId,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        if (!Guid.TryParse(confirmationId, out var operationId))
        {
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(confirmationId));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A send reason of at most 500 characters is required.", nameof(reason));
        }

        var current = await campaigns.GetAsync(conferenceId, campaignId, cancellationToken)
            ?? throw new KeyNotFoundException("Email campaign preview was not found.");
        if (!string.Equals(current.Value.CreatedByUserId, actor.UserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the user who previewed a campaign can confirm it.");
        }

        if (current.Value.State == EmailCampaignState.Ready &&
            current.Value.ConfirmedOperationId == operationId.ToString("N"))
        {
            if (!string.Equals(
                    current.Value.ConfirmationReason,
                    reason.Trim(),
                    StringComparison.Ordinal))
            {
                throw new RequestConflictException(
                    "The confirmation Idempotency-Key was already used with a different send reason.");
            }

            return current;
        }

        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("Email campaign preview changed. Reload before sending.");
        }

        var confirmed = current.Value.Confirm(
            actor.UserId,
            operationId.ToString("N"),
            reason,
            nowUtc);
        var outboxItems = confirmed.RecipientUserIds.Select(userId => new EmailOutboxItem(
            $"outbox:{confirmed.Id}:{userId}",
            conferenceId,
            userId,
            EmailCategory.ConferenceOperations,
            "conference-operations",
            EmailOutboxStatus.Ready,
            nowUtc.ToUniversalTime(),
            nowUtc.ToUniversalTime(),
            null,
            null,
            null,
            null,
            confirmed.Subject,
            confirmed.PlainTextContent,
            confirmed.SenderAddress)).ToArray();
        var auditEvent = PreviewTargetedEmailHandler.CreateAudit(
            conferenceId,
            actor.UserId,
            "EmailCampaignSent",
            campaignId,
            $"Targeted email campaign queued for {outboxItems.Length} recipients. Reason: {reason.Trim()}",
            nowUtc);
        return await campaigns.ConfirmAndEnqueueAsync(
            confirmed,
            expectedEtag,
            outboxItems,
            auditEvent,
            cancellationToken);
    }
}
