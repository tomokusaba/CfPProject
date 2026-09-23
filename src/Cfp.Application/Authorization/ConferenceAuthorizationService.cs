using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Authorization;

public interface IConferenceMembershipReader
{
    Task<ConferenceMembership?> GetMembershipAsync(
        string conferenceId,
        string userId,
        CancellationToken cancellationToken);
}

public sealed class ConferenceAuthorizationService(IConferenceMembershipReader membershipReader)
{
    public async Task<ConferenceMembership> RequireRoleAsync(
        string conferenceId,
        Actor actor,
        CancellationToken cancellationToken,
        params ConferenceRole[] allowedRoles)
    {
        var membership = await membershipReader.GetMembershipAsync(
            conferenceId,
            actor.UserId,
            cancellationToken);

        if (membership is null || !membership.IsActive || !allowedRoles.Contains(membership.Role))
        {
            throw new ConferenceAuthorizationException();
        }

        return membership;
    }
}

public sealed class ConferenceAuthorizationException()
    : Exception("The actor is not authorized for this conference.");
