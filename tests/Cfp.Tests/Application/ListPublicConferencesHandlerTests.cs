using Cfp.Application.PublicConferences;
using Cfp.Domain.Conferences;

namespace Cfp.Tests.Application;

public sealed class ListPublicConferencesHandlerTests
{
    [Fact]
    public async Task HandleAsync_ClampsPageSizeAndPassesContinuationToken()
    {
        var reader = new RecordingPublicConferenceReader();
        var handler = new ListPublicConferencesHandler(reader);
        using var cancellationSource = new CancellationTokenSource();
        var nowUtc = DateTimeOffset.UtcNow;

        await handler.HandleAsync(200, "cursor", "  systems  ", nowUtc, cancellationSource.Token);

        Assert.Equal(50, reader.PageSize);
        Assert.Equal("cursor", reader.ContinuationToken);
        Assert.Equal("systems", reader.SearchTerm);
        Assert.Equal(nowUtc, reader.NowUtc);
        Assert.Equal(cancellationSource.Token, reader.CancellationToken);
    }

    private sealed class RecordingPublicConferenceReader : IPublicConferenceReader
    {
        public int PageSize { get; private set; }
        public string? ContinuationToken { get; private set; }
        public string? SearchTerm { get; private set; }
        public DateTimeOffset NowUtc { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PublicConferencePage> ListAsync(
            int pageSize,
            string? continuationToken,
            string searchTerm,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            PageSize = pageSize;
            ContinuationToken = continuationToken;
            SearchTerm = searchTerm;
            NowUtc = nowUtc;
            CancellationToken = cancellationToken;
            return Task.FromResult(new PublicConferencePage([], null));
        }

        public Task<Conference?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult<Conference?>(null);
    }
}
