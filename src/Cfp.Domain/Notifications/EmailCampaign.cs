using Cfp.Domain.Proposals;

namespace Cfp.Domain.Notifications;

public enum EmailCampaignState
{
    Previewed,
    Ready
}

public sealed record EmailCampaign(
    string Id,
    string ConferenceId,
    string CreatedByUserId,
    IReadOnlyList<ProposalStatus> TargetProposalStatuses,
    IReadOnlyList<string> RecipientUserIds,
    string SenderAddress,
    string Subject,
    string PlainTextContent,
    int ExcludedRecipientCount,
    string RequestHash,
    EmailCampaignState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    string? ConfirmedOperationId,
    string? ConfirmationReason)
{
    public EmailCampaign Confirm(
        string actorUserId,
        string operationId,
        string reason,
        DateTimeOffset nowUtc)
    {
        if (!string.Equals(CreatedByUserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the campaign creator can confirm this preview.");
        }

        if (State != EmailCampaignState.Previewed || nowUtc.ToUniversalTime() >= ExpiresAtUtc)
        {
            throw new InvalidOperationException("Email campaign preview has expired or was already confirmed.");
        }

        if (RecipientUserIds.Count == 0)
        {
            throw new InvalidOperationException("There are no eligible recipients in this preview.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A send reason of at most 500 characters is required.", nameof(reason));
        }

        return this with
        {
            State = EmailCampaignState.Ready,
            ConfirmedAtUtc = nowUtc.ToUniversalTime(),
            ConfirmedOperationId = operationId,
            ConfirmationReason = reason.Trim()
        };
    }
}
