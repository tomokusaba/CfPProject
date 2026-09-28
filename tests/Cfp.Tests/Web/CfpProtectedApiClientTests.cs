using System.Net;
using System.Net.Http.Headers;
using Cfp.Contracts.V1;
using Cfp.Web.Api;
using Cfp.Web.Auth;
using MemoryPack;
using Microsoft.Extensions.Configuration;

namespace Cfp.Tests.Web;

public sealed class CfpProtectedApiClientTests
{
    [Fact]
    public async Task TargetedEmailMethods_UsePreviewAndConfirmationHeadersAndMemoryPack()
    {
        var responseCount = 0;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            Assert.Equal("application/octet-stream", request.Headers.Accept.Single().MediaType);
            Assert.Equal("application/octet-stream", request.Content?.Headers.ContentType?.MediaType);
            var requestBytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            if (request.RequestUri?.AbsolutePath.EndsWith("/mail/preview", StringComparison.Ordinal) == true)
            {
                responseCount++;
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(
                    "https://api.example.test/api/v1/manage/conferences/conference%201/mail/preview",
                    request.RequestUri?.AbsoluteUri);
                Assert.True(request.Headers.TryGetValues("Idempotency-Key", out var previewKeys));
                Assert.Equal("5b4c8e32-ef99-48df-831a-f9d3bb8f13a1", Assert.Single(previewKeys));
                var body = MemoryPackSerializer.Deserialize<PreviewTargetedEmailRequestDto>(requestBytes);
                Assert.Equal(["Accepted"], body?.ProposalStatuses);
                Assert.Equal("Schedule update", body?.Subject);
                return CreateResponse(HttpStatusCode.OK, new EmailCampaignPreviewDto
                {
                    CampaignId = "campaign_01",
                    EligibleRecipientCount = 3,
                    ExcludedRecipientCount = 1,
                    Subject = "Schedule update",
                    PlainTextContent = "The schedule is available.",
                    ExpiresAtUtc = "2026-01-01T00:30:00Z",
                    ETag = "\"preview-etag\"",
                    SenderAddress = "sender@example.test"
                });
            }

            responseCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://api.example.test/api/v1/manage/conferences/conference%201/mail",
                request.RequestUri?.AbsoluteUri);
            Assert.True(request.Headers.TryGetValues("Idempotency-Key", out var sendKeys));
            Assert.Equal("1da814a1-d6d1-433e-9c36-62e08ad65331", Assert.Single(sendKeys));
            Assert.True(request.Headers.TryGetValues("If-Match", out var etags));
            Assert.Equal("\"preview-etag\"", Assert.Single(etags));
            var sendBody = MemoryPackSerializer.Deserialize<SendTargetedEmailRequestDto>(requestBytes);
            Assert.Equal("campaign_01", sendBody?.CampaignId);
            Assert.Equal("Schedule announcement", sendBody?.Reason);
            return CreateResponse(HttpStatusCode.Accepted, new EmailCampaignSendResultDto
            {
                CampaignId = "campaign_01",
                State = "Ready",
                RecipientCount = 3
            });
        });
        var client = CreateClient(handler);

        var preview = await client.PreviewTargetedEmailAsync(
            "conference 1",
            new PreviewTargetedEmailRequestDto
            {
                ProposalStatuses = ["Accepted"],
                Subject = "Schedule update",
                PlainTextContent = "The schedule is available."
            },
            Guid.Parse("5b4c8e32-ef99-48df-831a-f9d3bb8f13a1"));
        var result = await client.SendTargetedEmailAsync(
            "conference 1",
            new SendTargetedEmailRequestDto { CampaignId = preview.CampaignId, Reason = "Schedule announcement" },
            preview.ETag,
            Guid.Parse("1da814a1-d6d1-433e-9c36-62e08ad65331"));

        Assert.Equal(2, responseCount);
        Assert.Equal(3, preview.EligibleRecipientCount);
        Assert.Equal("sender@example.test", preview.SenderAddress);
        Assert.Equal("Ready", result.State);
    }

    private static CfpProtectedApiClient CreateClient(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "https://api.example.test" })
            .Build();
        var authentication = new AuthenticationConfiguration(
            true,
            "https://tenant.example.test/",
            "8b8d46f3-4f36-4e38-87b0-c5985e93a116",
            "api://cfp-api/access");
        return new CfpProtectedApiClient(new HttpClient(handler), configuration, authentication);
    }

    private static HttpResponseMessage CreateResponse<T>(HttpStatusCode statusCode, T payload)
        where T : class
    {
        var content = new ByteArrayContent(MemoryPackSerializer.Serialize(payload));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new HttpResponseMessage(statusCode) { Content = content };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
