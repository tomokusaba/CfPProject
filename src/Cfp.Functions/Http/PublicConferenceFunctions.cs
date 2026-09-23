using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Web;
using Cfp.Application.PublicConferences;
using Cfp.Contracts.V1;
using Cfp.Domain.Conferences;
using MemoryPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class PublicConferenceFunctions(
    ListPublicConferencesHandler listHandler,
    GetPublicConferenceHandler getHandler)
{
    [Function(nameof(ListPublicConferences))]
    public async Task<HttpResponseData> ListPublicConferences(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/public/conferences")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var query = HttpUtility.ParseQueryString(request.Url.Query);
        var pageSize = 20;
        if (query["pageSize"] is { } pageSizeValue &&
            (!int.TryParse(pageSizeValue, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) ||
             pageSize is < 1 or > 50))
        {
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidPageSize",
                "Page size must be between 1 and 50.",
                cancellationToken);
        }

        var continuationToken = query["continuationToken"];
        if (continuationToken is { Length: > 4096 })
        {
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidContinuationToken",
                "The continuation token is too long.",
                cancellationToken);
        }

        var searchTerm = query["search"]?.Trim() ?? string.Empty;
        if (searchTerm.Length > 100)
        {
            return await WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidSearchTerm",
                "Search text must be 100 characters or fewer.",
                cancellationToken);
        }

        var result = await listHandler.HandleAsync(
            pageSize,
            continuationToken,
            searchTerm,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return await WriteMemoryPackAsync(
            request,
            HttpStatusCode.OK,
            new PublicConferencePageDto
            {
                Conferences = result.Items.Select(item => new PublicConferenceSummaryDto
                {
                    Slug = item.Slug,
                    Title = item.Title,
                    StartsAtUtc = item.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    TimeZoneId = item.TimeZoneId,
                    CfpAvailability = item.CfpAvailability.ToString()
                }).ToList(),
                ContinuationToken = result.ContinuationToken
            },
            cancellationToken);
    }

    [Function(nameof(GetPublicConference))]
    public async Task<HttpResponseData> GetPublicConference(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/public/conferences/{slug}")]
        HttpRequestData request,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var conference = await getHandler.HandleAsync(slug, cancellationToken);
        if (conference is null)
        {
            return await WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ConferenceNotFound",
                "This conference is not available.",
                cancellationToken);
        }

        return await WriteMemoryPackAsync(
            request,
            HttpStatusCode.OK,
            new PublicConferenceDto
            {
                Slug = conference.Slug,
                Title = conference.Title,
                Description = conference.Description,
                StartsAtUtc = conference.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                EndsAtUtc = conference.EndsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                TimeZoneId = conference.TimeZoneId,
                CfpAvailability = conference.GetCfpAvailability(DateTimeOffset.UtcNow).ToString(),
                CfpOpensAtUtc = conference.CfpOpensAtUtc.ToString("O", CultureInfo.InvariantCulture),
                CfpClosesAtUtc = conference.CfpClosesAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ConferenceId = conference.Id,
                PublicShowcaseEnabled = conference.PublicShowcaseEnabled
            },
            cancellationToken);
    }

    private static bool AcceptsMemoryPack(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("Accept", out var values))
        {
            return true;
        }

        return values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => MediaTypeWithQualityHeaderValue.TryParse(value, out var parsed) ? parsed : null)
            .Any(mediaType => mediaType is not null &&
                              mediaType.Quality != 0 &&
                              (string.Equals(mediaType.MediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(mediaType.MediaType, "*/*", StringComparison.Ordinal) ||
                               string.Equals(mediaType.MediaType, "application/*", StringComparison.Ordinal)));
    }

    private static Task<HttpResponseData> WriteErrorAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        WriteMemoryPackAsync(
            request,
            statusCode,
            new ApiErrorDto
            {
                Code = code,
                Message = message,
                CorrelationId = request.FunctionContext.InvocationId
            },
            cancellationToken);

    private static async Task<HttpResponseData> WriteMemoryPackAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/octet-stream");
        var payload = MemoryPackSerializer.Serialize(value);
        await response.Body.WriteAsync(payload, cancellationToken);
        return response;
    }
}
