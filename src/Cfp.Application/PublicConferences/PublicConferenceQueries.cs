using Cfp.Domain.Conferences;

namespace Cfp.Application.PublicConferences;

public sealed record PublicConferenceSummary(
    string Slug,
    string Title,
    DateTimeOffset StartsAtUtc,
    string TimeZoneId,
    CfpAvailability CfpAvailability);

public sealed record PublicConferencePage(
    IReadOnlyList<PublicConferenceSummary> Items,
    string? ContinuationToken);

public interface IPublicConferenceReader
{
    Task<PublicConferencePage> ListAsync(
        int pageSize,
        string? continuationToken,
        string searchTerm,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<Conference?> GetBySlugAsync(string slug, CancellationToken cancellationToken);
}

public sealed class ListPublicConferencesHandler(IPublicConferenceReader reader)
{
    public Task<PublicConferencePage> HandleAsync(
        int pageSize,
        string? continuationToken,
        string? searchTerm,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        reader.ListAsync(
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            searchTerm?.Trim() ?? string.Empty,
            nowUtc.ToUniversalTime(),
            cancellationToken);
}

public sealed class GetPublicConferenceHandler(IPublicConferenceReader reader)
{
    public Task<Conference?> HandleAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return reader.GetBySlugAsync(slug, cancellationToken);
    }
}
