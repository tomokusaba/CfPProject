using System.Globalization;
using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Conferences;
using Cfp.Application.Identity;
using Cfp.Contracts.V1;
using Cfp.Domain.Conferences;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class ConferenceLifecycleFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ConferenceAuthorizationService authorization,
    IConferenceLifecycleStore conferenceStore,
    PublishConferenceHandler publishHandler,
    SetCfpPublicationStateHandler cfpHandler,
    SetPublicShowcaseHandler showcaseHandler,
    UpdateConferenceHandler updateHandler,
    ArchiveConferenceHandler archiveHandler)
{
    [Function(nameof(GetManagedConference))]
    public async Task<HttpResponseData> GetManagedConference(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/manage/conferences/{conferenceId}")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        try
        {
            var membership = await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
            var conference = await conferenceStore.GetAsync(conferenceId, cancellationToken);
            if (conference is null)
            {
                return await MemoryPackHttp.WriteErrorAsync(
                    request,
                    HttpStatusCode.NotFound,
                    "ConferenceNotFound",
                    "The conference was not found.",
                    cancellationToken);
            }

            return await WriteConferenceAsync(request, conference, membership.Role, HttpStatusCode.OK, cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
    }

    [Function(nameof(UpdateConference))]
    public async Task<HttpResponseData> UpdateConference(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v1/manage/conferences/{conferenceId}")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        var etag = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(etag))
        {
            return await PreconditionRequiredAsync(request, cancellationToken);
        }

        UpdateConferenceRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<UpdateConferenceRequestDto>(request, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, exception.StatusCode, exception.Code, exception.Message, cancellationToken);
        }

        if (payload.Title is null || payload.Description is null || payload.TimeZoneId is null ||
            !TryParseDate(payload.StartsAtUtc, out var startsAtUtc) ||
            !TryParseDate(payload.EndsAtUtc, out var endsAtUtc) ||
            !TryParseDate(payload.CfpOpensAtUtc, out var cfpOpensAtUtc) ||
            !TryParseDate(payload.CfpClosesAtUtc, out var cfpClosesAtUtc))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidConference",
                "Valid conference fields and ISO-8601 timestamps with explicit UTC offsets are required.",
                cancellationToken);
        }

        try
        {
            var membership = await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
            var updated = await updateHandler.HandleAsync(
                conferenceId,
                actor,
                payload.Title,
                payload.Description,
                payload.TimeZoneId,
                startsAtUtc,
                endsAtUtc,
                cfpOpensAtUtc,
                cfpClosesAtUtc,
                etag,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await WriteConferenceAsync(request, updated, membership.Role, HttpStatusCode.OK, cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await ConferenceNotFoundAsync(request, cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await ConflictAsync(request, exception, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, HttpStatusCode.BadRequest, "InvalidConference", exception.Message, cancellationToken);
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

    [Function(nameof(ArchiveConference))]
    public async Task<HttpResponseData> ArchiveConference(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/archive")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        var etag = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(etag))
        {
            return await PreconditionRequiredAsync(request, cancellationToken);
        }

        ArchiveConferenceRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<ArchiveConferenceRequestDto>(request, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, exception.StatusCode, exception.Code, exception.Message, cancellationToken);
        }

        try
        {
            var updated = await archiveHandler.HandleAsync(
                conferenceId,
                actor,
                payload.Reason ?? string.Empty,
                etag,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await WriteConferenceAsync(request, updated, ConferenceRole.ConferenceOwner, HttpStatusCode.OK, cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await ConferenceNotFoundAsync(request, cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await ConflictAsync(request, exception, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, HttpStatusCode.BadRequest, "InvalidArchiveReason", exception.Message, cancellationToken);
        }
    }

    [Function(nameof(PublishConference))]
    public Task<HttpResponseData> PublishConference(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/publish")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken) =>
        ChangeCfpOrVisibilityAsync(request, conferenceId, cancellationToken, isCfpUpdate: false);

    [Function(nameof(SetCfpPublicationState))]
    public Task<HttpResponseData> SetCfpPublicationState(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/manage/conferences/{conferenceId}/cfp")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken) =>
        ChangeCfpOrVisibilityAsync(request, conferenceId, cancellationToken, isCfpUpdate: true);

    private async Task<HttpResponseData> ChangeCfpOrVisibilityAsync(
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken,
        bool isCfpUpdate)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        var etag = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(etag))
        {
            return await PreconditionRequiredAsync(request, cancellationToken);
        }

        try
        {
            var membership = await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
            Versioned<Conference> updated;
            if (isCfpUpdate)
            {
                SetCfpPublicationStateRequestDto payload;
                try
                {
                    payload = await MemoryPackHttp.ReadAsync<SetCfpPublicationStateRequestDto>(request, cancellationToken);
                }
                catch (MemoryPackRequestException exception)
                {
                    return await MemoryPackHttp.WriteErrorAsync(request, exception.StatusCode, exception.Code, exception.Message, cancellationToken);
                }

                if (payload.State is null ||
                    !Enum.TryParse<CfpPublicationState>(payload.State, ignoreCase: false, out var state))
                {
                    return await MemoryPackHttp.WriteErrorAsync(
                        request,
                        HttpStatusCode.BadRequest,
                        "InvalidCfpState",
                        "CFP state must be Draft, Published, or ManuallyClosed.",
                        cancellationToken);
                }

                updated = await cfpHandler.HandleAsync(
                    conferenceId,
                    actor,
                    state,
                    payload.Reason ?? string.Empty,
                    etag,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }
            else
            {
                updated = await publishHandler.HandleAsync(
                    conferenceId,
                    actor,
                    etag,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }

            return await WriteConferenceAsync(request, updated, membership.Role, HttpStatusCode.OK, cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await ConferenceNotFoundAsync(request, cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await ConflictAsync(request, exception, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, HttpStatusCode.BadRequest, "InvalidConferenceState", exception.Message, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, HttpStatusCode.Conflict, "ConferenceStateConflict", exception.Message, cancellationToken);
        }
    }

    [Function(nameof(SetPublicShowcase))]
    public async Task<HttpResponseData> SetPublicShowcase(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/manage/conferences/{conferenceId}/showcase")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        var etag = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(etag))
        {
            return await PreconditionRequiredAsync(request, cancellationToken);
        }

        SetPublicShowcaseRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SetPublicShowcaseRequestDto>(request, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, exception.StatusCode, exception.Code, exception.Message, cancellationToken);
        }

        try
        {
            var membership = await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
            var updated = await showcaseHandler.HandleAsync(
                conferenceId,
                actor,
                payload.Enabled,
                payload.Reason ?? string.Empty,
                etag,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await WriteConferenceAsync(request, updated, membership.Role, HttpStatusCode.OK, cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await ConferenceNotFoundAsync(request, cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await ConflictAsync(request, exception, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(request, HttpStatusCode.BadRequest, "InvalidShowcaseSetting", exception.Message, cancellationToken);
        }
    }

    private async Task<Actor?> ResolveActorAsync(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var identity = principalReader.TryReadIdentity(request);
        return identity is null
            ? null
            : await actorResolution.ResolveAsync(identity, cancellationToken);
    }

    private static Task<HttpResponseData> WriteConferenceAsync(
        HttpRequestData request,
        Versioned<Conference> versioned,
        ConferenceRole role,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        var conference = versioned.Value;
        return WriteConferenceAsyncCore(request, conference, versioned.ETag, role, statusCode, cancellationToken);
    }

    private static async Task<HttpResponseData> WriteConferenceAsyncCore(
        HttpRequestData request,
        Conference conference,
        string etag,
        ConferenceRole role,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        var response = await MemoryPackHttp.WriteAsync(
            request,
            statusCode,
            new ManagedConferenceDto
            {
                Id = conference.Id,
                Slug = conference.Slug,
                Title = conference.Title,
                Description = conference.Description,
                LifecycleState = conference.LifecycleState.ToString(),
                Visibility = conference.Visibility.ToString(),
                CfpAvailability = conference.GetCfpAvailability(DateTimeOffset.UtcNow).ToString(),
                TimeZoneId = conference.TimeZoneId,
                StartsAtUtc = conference.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                EndsAtUtc = conference.EndsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                CfpOpensAtUtc = conference.CfpOpensAtUtc.ToString("O", CultureInfo.InvariantCulture),
                CfpClosesAtUtc = conference.CfpClosesAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ETag = etag,
                Role = role.ToString(),
                PublicShowcaseEnabled = conference.PublicShowcaseEnabled
            },
            cancellationToken);
        response.Headers.Add("ETag", etag);
        return response;
    }

    private static Task<HttpResponseData> UnauthorizedAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Unauthorized,
            "AuthenticationRequired",
            "Sign in to manage this conference.",
            cancellationToken);

    private static Task<HttpResponseData> ForbiddenAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Forbidden,
            "ConferenceRoleRequired",
            "You do not have permission to manage this conference.",
            cancellationToken);

    private static Task<HttpResponseData> PreconditionRequiredAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.PreconditionRequired,
            "IfMatchRequired",
            "Send the current conference ETag in If-Match.",
            cancellationToken);

    private static Task<HttpResponseData> ConferenceNotFoundAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.NotFound,
            "ConferenceNotFound",
            "The conference was not found.",
            cancellationToken);

    private static Task<HttpResponseData> ConflictAsync(
        HttpRequestData request,
        RequestConflictException exception,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.PreconditionFailed,
            "VersionConflict",
            exception.Message,
            cancellationToken);

    private static string? GetSingleHeader(HttpRequestData request, string name)
    {
        if (!request.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var headers = values.ToArray();
        return headers.Length == 1 ? headers[0] : null;
    }

    private static bool TryParseDate(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hasOffset = value.EndsWith('Z') ||
                        value.EndsWith('z') ||
                        value.Length >= 6 &&
                        (value[^6] == '+' || value[^6] == '-') &&
                        value[^3] == ':';
        return hasOffset &&
               DateTimeOffset.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out result);
    }
}
