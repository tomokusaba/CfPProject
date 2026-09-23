using System.Globalization;
using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.Scheduling;
using Cfp.Contracts.V1;
using Cfp.Domain.Conferences;
using Cfp.Domain.Scheduling;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class TimetableFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ConferenceAuthorizationService authorization,
    IScheduleStore scheduleStore,
    SaveScheduleDraftHandler saveDraftHandler,
    PublishScheduleHandler publishHandler,
    GetPublicScheduleHandler publicScheduleHandler)
{
    [Function(nameof(GetManagedSchedule))]
    public async Task<HttpResponseData> GetManagedSchedule(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/schedule")]
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
            await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
            var draft = await scheduleStore.GetDraftAsync(conferenceId, cancellationToken);
            var published = await scheduleStore.GetPublishedAsync(conferenceId, cancellationToken);
            var plan = draft?.Value;
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                plan is null
                    ? new ManagedScheduleDto
                    {
                        PublishedRevision = published?.Value.Revision,
                        PublicationETag = published?.ETag
                    }
                    : ToManagedDto(plan, draft!.ETag, published),
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
    }

    [Function(nameof(SaveScheduleDraft))]
    public async Task<HttpResponseData> SaveScheduleDraft(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/manage/conferences/{conferenceId}/schedule")]
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

        SaveScheduleDraftRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SaveScheduleDraftRequestDto>(request, cancellationToken);
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

        if (payload.Rooms is null || payload.Tracks is null || payload.Slots is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidSchedule",
                "Rooms, tracks, and slots are required.",
                cancellationToken);
        }

        var currentDraft = await scheduleStore.GetDraftAsync(conferenceId, cancellationToken);
        var ifMatch = GetSingleHeader(request, "If-Match");
        if ((currentDraft is not null && string.IsNullOrWhiteSpace(ifMatch)) ||
            (currentDraft is null && ifMatch is not null))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionRequired,
                "IfMatchRequired",
                "Send the current draft ETag in If-Match when updating a schedule.",
                cancellationToken);
        }

        var slots = new List<ScheduleSlotInput>();
        foreach (var slot in payload.Slots)
        {
            if (slot is null ||
                string.IsNullOrWhiteSpace(slot.Id) ||
                string.IsNullOrWhiteSpace(slot.ProposalId) ||
                string.IsNullOrWhiteSpace(slot.RoomId) ||
                !TryParseDate(slot.StartsAtUtc, out var startsAtUtc) ||
                !TryParseDate(slot.EndsAtUtc, out var endsAtUtc))
            {
                return await MemoryPackHttp.WriteErrorAsync(
                    request,
                    HttpStatusCode.BadRequest,
                    "InvalidScheduleSlot",
                    "Each slot requires IDs and ISO-8601 start/end timestamps with an explicit UTC offset.",
                    cancellationToken);
            }

            slots.Add(new ScheduleSlotInput(
                slot.Id,
                slot.ProposalId,
                slot.RoomId,
                slot.TrackId,
                startsAtUtc,
                endsAtUtc));
        }

        try
        {
            var saved = await saveDraftHandler.HandleAsync(
                conferenceId,
                actor,
                new SaveScheduleDraftCommand(
                    payload.Rooms.Select(room => new ScheduleRoom(room.Id, room.Name)).ToArray(),
                    payload.Tracks.Select(track => new ScheduleTrack(track.Id, track.Name)).ToArray(),
                    slots),
                ifMatch,
                DateTimeOffset.UtcNow,
                cancellationToken);
            var published = await scheduleStore.GetPublishedAsync(conferenceId, cancellationToken);
            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                ToManagedDto(saved.Value, saved.ETag, published),
                cancellationToken);
            response.Headers.Add("ETag", saved.ETag);
            return response;
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ScheduleReferenceNotFound",
                exception.Message,
                cancellationToken);
        }
        catch (ScheduleValidationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.UnprocessableEntity,
                "ScheduleConflict",
                string.Join(" ", exception.Errors),
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionFailed,
                "VersionConflict",
                exception.Message,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidSchedule",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "ScheduleStateConflict",
                exception.Message,
                cancellationToken);
        }
    }

    [Function(nameof(PublishSchedule))]
    public async Task<HttpResponseData> PublishSchedule(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/schedule/publish")]
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

        var draftEtag = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(draftEtag))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionRequired,
                "IfMatchRequired",
                "Send the current draft ETag in If-Match before publishing.",
                cancellationToken);
        }

        try
        {
            var published = await publishHandler.HandleAsync(
                conferenceId,
                actor,
                draftEtag,
                GetSingleHeader(request, "If-Match-Publication"),
                DateTimeOffset.UtcNow,
                cancellationToken);
            var draft = await scheduleStore.GetDraftAsync(conferenceId, cancellationToken);
            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                ToManagedDto(published.Value, draft?.ETag ?? string.Empty, published),
                cancellationToken);
            response.Headers.Add("ETag", published.ETag);
            return response;
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ScheduleNotFound",
                exception.Message,
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionFailed,
                "VersionConflict",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "ScheduleStateConflict",
                exception.Message,
                cancellationToken);
        }
    }

    [Function(nameof(GetPublicSchedule))]
    public async Task<HttpResponseData> GetPublicSchedule(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/public/conferences/{slug}/schedule")]
        HttpRequestData request,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var schedule = await publicScheduleHandler.HandleAsync(slug, cancellationToken);
        if (schedule is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ConferenceNotFound",
                "This conference is not available.",
                cancellationToken);
        }

        return await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new PublicScheduleDto
            {
                ConferenceTitle = schedule.ConferenceTitle,
                TimeZoneId = schedule.TimeZoneId,
                Sessions = schedule.Sessions.Select(session => new PublicScheduleSessionDto
                {
                    SessionId = session.SessionId,
                    Title = session.Title,
                    Abstract = session.Abstract,
                    Speakers = session.Speakers,
                    RoomName = session.RoomName,
                    TrackName = session.TrackName,
                    StartsAtUtc = session.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    EndsAtUtc = session.EndsAtUtc.ToString("O", CultureInfo.InvariantCulture)
                }).ToList()
            },
            cancellationToken);
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

    private static ManagedScheduleDto ToManagedDto(
        SchedulePlan plan,
        string draftEtag,
        Versioned<SchedulePlan>? publication) => new()
    {
        Revision = plan.Revision,
        Rooms = plan.Rooms.Select(room => new ScheduleRoomDto { Id = room.Id, Name = room.Name }).ToList(),
        Tracks = plan.Tracks.Select(track => new ScheduleTrackDto { Id = track.Id, Name = track.Name }).ToList(),
        Slots = plan.Slots.Select(slot => new ScheduleSlotInputDto
        {
            Id = slot.Id,
            ProposalId = slot.ProposalId,
            RoomId = slot.RoomId,
            TrackId = slot.TrackId,
            StartsAtUtc = slot.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
            EndsAtUtc = slot.EndsAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }).ToList(),
        DraftETag = draftEtag,
        PublishedRevision = publication?.Value.Revision,
        PublicationETag = publication?.ETag
    };

    private static Task<HttpResponseData> UnauthorizedAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Unauthorized,
            "AuthenticationRequired",
            "Sign in to manage or view this schedule.",
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
