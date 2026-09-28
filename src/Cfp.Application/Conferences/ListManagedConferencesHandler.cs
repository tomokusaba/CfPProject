using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Conferences;

public sealed record ManagedConferenceSummary(
    Conference Conference,
    string ETag,
    ConferenceRole Role);

public sealed record ManagedConferencePage(
    IReadOnlyList<ManagedConferenceSummary> Conferences,
    string? ContinuationToken);

public interface IManagedConferenceReader
{
    Task<ManagedConferencePage> ListAsync(
        string userId,
        int pageSize,
        string? continuationToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed class ListManagedConferencesHandler(IManagedConferenceReader reader)
{
    public Task<ManagedConferencePage> HandleAsync(
        Actor actor,
        int pageSize,
        string? continuationToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        reader.ListAsync(
            actor.UserId,
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            nowUtc.ToUniversalTime(),
            cancellationToken);
}
