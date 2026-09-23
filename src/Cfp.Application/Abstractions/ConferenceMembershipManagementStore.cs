using Cfp.Application.Auditing;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Abstractions;

public interface IConferenceMembershipManagementStore
{
    Task<IReadOnlyList<ConferenceMembership>> ListAsync(
        string conferenceId,
        CancellationToken cancellationToken);

    Task<Versioned<ConferenceMembership>> SetRoleAsync(
        string conferenceId,
        string userId,
        ConferenceRole role,
        string expectedRosterEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
