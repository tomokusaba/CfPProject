using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Abstractions;
using Cfp.Application.Conferences;
using Cfp.Application.Identity;
using Cfp.Contracts.V1;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class ConferenceManagementFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    CreateConferenceHandler createConferenceHandler,
    ListManagedConferencesHandler listHandler)
{
    [Function(nameof(ListManagedConferences))]
    public async Task<HttpResponseData> ListManagedConferences(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/me/conferences")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var identity = principalReader.TryReadIdentity(request);
        if (identity is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Unauthorized,
                "AuthenticationRequired",
                "Sign in to see managed conferences.",
                cancellationToken);
        }

        var actor = await actorResolution.ResolveAsync(identity, cancellationToken);
        var query = HttpUtility.ParseQueryString(request.Url.Query);
        var pageSize = 20;
        if (query["pageSize"] is { } size &&
            (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) ||
             pageSize is < 1 or > 50))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidPageSize",
                "Page size must be between 1 and 50.",
                cancellationToken);
        }

        var continuationToken = query["continuationToken"];
        if (continuationToken is { Length: > 4096 })
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidContinuationToken",
                "The continuation token is too long.",
                cancellationToken);
        }

        var page = await listHandler.HandleAsync(
            actor,
            pageSize,
            continuationToken,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new ManagedConferencesDto
            {
                Conferences = page.Conferences.Select(item => new ManagedConferenceSummaryDto
                {
                    Id = item.Conference.Id,
                    Slug = item.Conference.Slug,
                    Title = item.Conference.Title,
                    LifecycleState = item.Conference.LifecycleState.ToString(),
                    Visibility = item.Conference.Visibility.ToString(),
                    CfpAvailability = item.Conference.GetCfpAvailability(DateTimeOffset.UtcNow).ToString(),
                    StartsAtUtc = item.Conference.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    TimeZoneId = item.Conference.TimeZoneId,
                    Role = item.Role.ToString()
                }).ToList(),
                ContinuationToken = page.ContinuationToken
            },
            cancellationToken);
    }

    [Function(nameof(CreateConference))]
    public async Task<HttpResponseData> CreateConference(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        Actor actor;
        try
        {
            var identity = principalReader.TryReadIdentity(request);
            if (identity is null)
            {
                return await MemoryPackHttp.WriteErrorAsync(
                    request,
                    HttpStatusCode.Unauthorized,
                    "AuthenticationRequired",
                    "Sign in to create a conference.",
                    cancellationToken);
            }

            actor = await actorResolution.ResolveAsync(identity, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                cancellationToken);
        }

        var idempotencyKey = GetIdempotencyKey(request);
        if (idempotencyKey is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "IdempotencyKeyRequired",
                "A GUID Idempotency-Key header is required.",
                cancellationToken);
        }

        CreateConferenceRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<CreateConferenceRequestDto>(request, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                cancellationToken);
        }

        if (!TryParseDate(payload.StartsAtUtc, out var startsAtUtc) ||
            !TryParseDate(payload.EndsAtUtc, out var endsAtUtc) ||
            !TryParseDate(payload.CfpOpensAtUtc, out var cfpOpensAtUtc) ||
            !TryParseDate(payload.CfpClosesAtUtc, out var cfpClosesAtUtc))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidDate",
                "All dates must be ISO-8601 timestamps with an explicit UTC offset.",
                cancellationToken);
        }

        if (payload.Slug is null ||
            payload.Title is null ||
            payload.Description is null ||
            payload.TimeZoneId is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidConference",
                "Conference text fields are required.",
                cancellationToken);
        }

        try
        {
            var created = await createConferenceHandler.HandleAsync(
                new CreateConferenceCommand(
                    payload.Slug,
                    payload.Title,
                    payload.Description,
                    payload.TimeZoneId,
                    startsAtUtc,
                    endsAtUtc,
                    cfpOpensAtUtc,
                    cfpClosesAtUtc),
                actor,
                idempotencyKey,
                DateTimeOffset.UtcNow,
                cancellationToken);

            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.Created,
                new CreatedConferenceDto
                {
                    Id = created.Id,
                    Slug = created.Slug,
                    LifecycleState = created.LifecycleState.ToString(),
                    Visibility = created.Visibility.ToString()
                },
                cancellationToken);
            response.Headers.Add("Location", $"/api/v1/manage/conferences/{Uri.EscapeDataString(created.Id)}");
            return response;
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "ConferenceConflict",
                exception.Message,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidConference",
                exception.Message,
                cancellationToken);
        }
        catch (TimeZoneNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidTimeZone",
                "The supplied time zone is not supported.",
                cancellationToken);
        }
    }

    private static string? GetIdempotencyKey(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("Idempotency-Key", out var values))
        {
            return null;
        }

        var headers = values.ToArray();
        if (headers.Length != 1)
        {
            return null;
        }

        var value = headers[0];
        return Guid.TryParse(value, out var guid) ? guid.ToString("N") : null;
    }

    private static bool TryParseDate(string value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        var hasExplicitOffset = value.EndsWith('Z') ||
                                value.EndsWith('z') ||
                                value.Length >= 6 &&
                                (value[^6] == '+' || value[^6] == '-') &&
                                value[^3] == ':';
        return hasExplicitOffset &&
               DateTimeOffset.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out result);
    }
}
