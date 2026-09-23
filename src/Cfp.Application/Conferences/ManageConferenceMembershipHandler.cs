using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Conferences;

public sealed class ManageConferenceMembershipHandler(
    ConferenceAuthorizationService authorization,
    IConferenceMembershipManagementStore memberships)
{
    public async Task<Versioned<ConferenceMembership>> HandleAsync(
        string conferenceId,
        string userId,
        ConferenceRole role,
        string expectedRosterEtag,
        string reason,
        Actor actor,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        if (role == ConferenceRole.Speaker)
        {
            throw new ArgumentException("Speaker is a self-service role and cannot be assigned by an owner.");
        }

        if (string.IsNullOrWhiteSpace(expectedRosterEtag))
        {
            throw new ArgumentException("The current owner roster ETag is required.", nameof(expectedRosterEtag));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A reason of at most 500 characters is required.", nameof(reason));
        }

        var auditEvent = new AuditEvent(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actor.UserId,
            "ConferenceMembershipChanged",
            $"membership:{userId}",
            nowUtc.ToUniversalTime(),
            $"Member role set to {role}. Reason: {reason.Trim()}");

        return await memberships.SetRoleAsync(
            conferenceId,
            userId,
            role,
            expectedRosterEtag,
            auditEvent,
            cancellationToken);
    }
}
