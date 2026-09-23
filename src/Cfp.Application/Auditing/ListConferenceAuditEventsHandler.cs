using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Auditing;

public sealed class ListConferenceAuditEventsHandler(
    ConferenceAuthorizationService authorization,
    IAuditEventReader reader)
{
    public async Task<AuditEventPage> HandleAsync(
        string conferenceId,
        Actor actor,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        return await reader.ListAsync(
            conferenceId,
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            cancellationToken);
    }
}
