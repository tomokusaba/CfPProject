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

public sealed class ConferenceMemberFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ConferenceAuthorizationService authorization,
    IConferenceLifecycleStore conferences,
    IConferenceMembershipManagementStore memberships,
    ManageConferenceMembershipHandler manageHandler)
{
    [Function(nameof(ListConferenceMembers))]
    public async Task<HttpResponseData> ListConferenceMembers(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/members")]
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
                ConferenceRole.ConferenceOwner);
            var conference = await conferences.GetAsync(conferenceId, cancellationToken);
            if (conference is null)
            {
                return await MemoryPackHttp.WriteErrorAsync(
                    request,
                    HttpStatusCode.NotFound,
                    "ConferenceNotFound",
                    "The conference was not found.",
                    cancellationToken);
            }

            var members = await memberships.ListAsync(conferenceId, cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ConferenceMembersDto
                {
                    Members = members.Select(member => new ConferenceMemberDto
                    {
                        UserId = member.UserId,
                        Role = member.Role.ToString(),
                        IsActive = member.IsActive
                    }).ToList(),
                    ConferenceETag = conference.ETag
                },
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
    }

    [Function(nameof(SetConferenceMember))]
    public async Task<HttpResponseData> SetConferenceMember(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/manage/conferences/{conferenceId}/members/{userId}")]
        HttpRequestData request,
        string conferenceId,
        string userId,
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

        var expectedEtag = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(expectedEtag))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionRequired,
                "IfMatchRequired",
                "Send the current conference ETag in If-Match.",
                cancellationToken);
        }

        SetConferenceMemberRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SetConferenceMemberRequestDto>(request, cancellationToken);
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

        if (payload.Role is null ||
            !Enum.TryParse<ConferenceRole>(payload.Role, ignoreCase: false, out var role) ||
            payload.Reason is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidMembership",
                "Role and reason are required.",
                cancellationToken);
        }

        try
        {
            var updated = await manageHandler.HandleAsync(
                conferenceId,
                userId,
                role,
                expectedEtag,
                payload.Reason,
                actor,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ConferenceMemberUpdatedDto
                {
                    UserId = updated.Value.UserId,
                    Role = updated.Value.Role.ToString(),
                    ConferenceETag = updated.ETag
                },
                cancellationToken);
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
                "UserNotFound",
                exception.Message,
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionFailed,
                "RosterConflict",
                exception.Message,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidMembership",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "OwnerRequirement",
                exception.Message,
                cancellationToken);
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

    private static Task<HttpResponseData> UnauthorizedAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Unauthorized,
            "AuthenticationRequired",
            "Sign in to manage conference members.",
            cancellationToken);

    private static Task<HttpResponseData> ForbiddenAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Forbidden,
            "ConferenceOwnerRequired",
            "Only a conference owner can manage members.",
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
}
