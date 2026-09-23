using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Cfp.Application.PublicConferences;
using Cfp.Application.Proposals;
using Cfp.Application.Scheduling;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class PublicShareMetadataFunctions(
    GetPublicConferenceHandler conferenceHandler,
    GetPublicScheduleHandler scheduleHandler,
    GetPublicProposalHandler proposalHandler)
{
    [Function(nameof(ShareConference))]
    public async Task<HttpResponseData> ShareConference(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "share/conferences/{slug}")]
        HttpRequestData request,
        string slug,
        CancellationToken cancellationToken)
    {
        var conference = await conferenceHandler.HandleAsync(slug, cancellationToken);
        if (conference is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        var publicUrl = $"{request.Url.GetLeftPart(UriPartial.Authority)}/c/{Uri.EscapeDataString(conference.Slug)}";
        var html = CreateHtml(
            conference.Title,
            conference.Description,
            publicUrl,
            $"{publicUrl}/schedule");
        return await WriteHtmlAsync(request, html, cancellationToken);
    }

    [Function(nameof(ShareSession))]
    public async Task<HttpResponseData> ShareSession(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "share/conferences/{slug}/sessions/{sessionId}")]
        HttpRequestData request,
        string slug,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var schedule = await scheduleHandler.HandleAsync(slug, cancellationToken);
        var session = schedule?.Sessions.FirstOrDefault(
            item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
        if (schedule is null || session is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        var publicUrl = $"{request.Url.GetLeftPart(UriPartial.Authority)}/c/{Uri.EscapeDataString(slug)}/sessions/{Uri.EscapeDataString(sessionId)}";
        var html = CreateHtml(
            session.Title,
            session.Abstract,
            publicUrl,
            $"{request.Url.GetLeftPart(UriPartial.Authority)}/c/{Uri.EscapeDataString(slug)}/schedule");
        return await WriteHtmlAsync(request, html, cancellationToken);
    }

    [Function(nameof(ShareProposal))]
    public async Task<HttpResponseData> ShareProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "share/conferences/{slug}/proposals/{proposalId}")]
        HttpRequestData request,
        string slug,
        string proposalId,
        CancellationToken cancellationToken)
    {
        var proposal = await proposalHandler.HandleAsync(slug, proposalId, cancellationToken);
        if (proposal is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        var authority = request.Url.GetLeftPart(UriPartial.Authority);
        var publicUrl = $"{authority}/c/{Uri.EscapeDataString(slug)}/proposals/{Uri.EscapeDataString(proposalId)}";
        var html = CreateHtml(
            proposal.Title,
            proposal.Abstract,
            publicUrl,
            $"{authority}/c/{Uri.EscapeDataString(slug)}/proposals");
        return await WriteHtmlAsync(request, html, cancellationToken);
    }

    public static string CreateHtml(string title, string description, string shareUrl, string appUrl)
    {
        var safeTitle = HtmlEncoder.Default.Encode(title);
        var safeDescription = HtmlEncoder.Default.Encode(Truncate(description, 280));
        var safeShareUrl = HtmlEncoder.Default.Encode(shareUrl);
        var safeAppUrl = HtmlEncoder.Default.Encode(appUrl);

        return $$"""
            <!doctype html>
            <html lang="ja">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{safeTitle}}</title>
              <meta name="description" content="{{safeDescription}}">
              <meta property="og:type" content="website">
              <meta property="og:title" content="{{safeTitle}}">
              <meta property="og:description" content="{{safeDescription}}">
              <meta property="og:url" content="{{safeShareUrl}}">
              <meta name="twitter:card" content="summary">
              <link rel="canonical" href="{{safeShareUrl}}">
            </head>
            <body>
              <main>
                <h1>{{safeTitle}}</h1>
                <p>{{safeDescription}}</p>
                <a href="{{safeAppUrl}}">カンファレンス情報を開く</a>
              </main>
            </body>
            </html>
            """;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static async Task<HttpResponseData> WriteHtmlAsync(
        HttpRequestData request,
        string html,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        response.Headers.Add("Cache-Control", "public, max-age=60, s-maxage=60");
        response.Headers.Add("X-Content-Type-Options", "nosniff");
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(html), cancellationToken);
        return response;
    }
}
