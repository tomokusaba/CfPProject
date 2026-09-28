using Cfp.Application.Auditing;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Abstractions;

public interface IConferenceManagementStore
{
    Task<Conference> CreateAsync(
        Conference conference,
        ConferenceMembership ownerMembership,
        AuditEvent auditEvent,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken);
}

public sealed class RequestConflictException(string message) : Exception(message);
