using Cfp.Application.Auditing;
using Cfp.Domain.Scheduling;

namespace Cfp.Application.Abstractions;

public interface IScheduleStore
{
    Task<Versioned<SchedulePlan>?> GetDraftAsync(
        string conferenceId,
        CancellationToken cancellationToken);

    Task<Versioned<SchedulePlan>?> GetPublishedAsync(
        string conferenceId,
        CancellationToken cancellationToken);

    Task<Versioned<SchedulePlan>> SaveDraftAsync(
        SchedulePlan plan,
        string? expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<Versioned<SchedulePlan>> PublishAsync(
        SchedulePlan plan,
        string expectedDraftEtag,
        string? expectedPublicationEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
