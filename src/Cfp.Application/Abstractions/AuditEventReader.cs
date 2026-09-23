using Cfp.Application.Auditing;

namespace Cfp.Application.Abstractions;

public sealed record AuditEventPage(
    IReadOnlyList<AuditEvent> Events,
    string? ContinuationToken);

public interface IAuditEventReader
{
    Task<AuditEventPage> ListAsync(
        string conferenceId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);
}
