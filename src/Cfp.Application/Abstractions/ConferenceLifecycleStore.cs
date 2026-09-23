using Cfp.Application.Auditing;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Abstractions;

public interface IConferenceLifecycleStore
{
    Task<Versioned<Conference>?> GetAsync(
        string conferenceId,
        CancellationToken cancellationToken);

    Task<Versioned<Conference>> SaveAsync(
        Conference conference,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
