using System.Net;
using System.Net.Http.Headers;
using Cfp.Contracts.V1;
using Cfp.Web.Api;
using MemoryPack;
using Microsoft.Extensions.Configuration;

namespace Cfp.Tests.Web;

public sealed class CfpApiClientTests
{
    [Fact]
    public async Task ListPublicConferencesAsync_UsesMemoryPackAndEscapesSearchText()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(
                "https://api.example.test/api/v1/public/conferences?pageSize=20&search=cloud%20%26%20systems",
                request.RequestUri?.AbsoluteUri);
            Assert.Contains(
                request.Headers.Accept,
                header => header.MediaType == "application/octet-stream");

            var content = new ByteArrayContent(MemoryPackSerializer.Serialize(new PublicConferencePageDto()));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = CreateClient(handler, "https://api.example.test");

        var result = await client.ListPublicConferencesAsync(searchTerm: "cloud & systems");

        Assert.Empty(result.Conferences);
    }

    [Fact]
    public async Task GetPublicConferenceAsync_UsesSafeApiErrorMessage()
    {
        var content = new ByteArrayContent(MemoryPackSerializer.Serialize(new ApiErrorDto
        {
            Code = "ConferenceNotFound",
            Message = "This conference is not available."
        }));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = content
        });
        var client = CreateClient(handler, "https://api.example.test/");

        var exception = await Assert.ThrowsAsync<CfpApiException>(
            () => client.GetPublicConferenceAsync("private-conf"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("This conference is not available.", exception.UserMessage);
    }

    [Theory]
    [InlineData("http://api.example.test")]
    [InlineData("ftp://localhost:7071")]
    [InlineData("https://user:password@api.example.test")]
    public async Task ListPublicConferencesAsync_RejectsUnsafeOrInvalidApiBaseAddress(string apiBaseUrl)
    {
        var client = CreateClient(new StubHandler(_ => throw new InvalidOperationException()), apiBaseUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListPublicConferencesAsync());
    }

    private static CfpApiClient CreateClient(HttpMessageHandler handler, string apiBaseUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = apiBaseUrl })
            .Build();

        return new CfpApiClient(new HttpClient(handler), configuration);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
