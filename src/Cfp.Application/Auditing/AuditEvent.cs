namespace Cfp.Application.Auditing;

public sealed record AuditEvent(
    string Id,
    string ConferenceId,
    string ActorUserId,
    string Operation,
    string TargetId,
    DateTimeOffset OccurredAtUtc,
    string Summary);
