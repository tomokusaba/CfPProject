using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Identity;
using Cfp.Application.Profiles;
using Cfp.Contracts.V1;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class SpeakerProfileFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    GetSpeakerProfileHandler getHandler,
    UpdateSpeakerProfileHandler updateHandler)
{
    [Function(nameof(GetSpeakerProfile))]
    public async Task<HttpResponseData> GetSpeakerProfile(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/me/profile")]
        HttpRequestData request,
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
            var profile = await getHandler.HandleAsync(actor, cancellationToken);
            return await WriteProfileAsync(request, profile, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProfileNotFound",
                "Your profile could not be found.",
                cancellationToken);
        }
    }

    [Function(nameof(UpdateSpeakerProfile))]
    public async Task<HttpResponseData> UpdateSpeakerProfile(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/me/profile")]
        HttpRequestData request,
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

        var ifMatch = GetSingleHeader(request, "If-Match");
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionRequired,
                "IfMatchRequired",
                "Send your current profile ETag in If-Match.",
                cancellationToken);
        }

        UpdateSpeakerProfileRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<UpdateSpeakerProfileRequestDto>(request, cancellationToken);
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

        if (payload.DisplayName is null || payload.Biography is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidProfile",
                "Display name and biography are required.",
                cancellationToken);
        }

        try
        {
            var updated = await updateHandler.HandleAsync(
                actor,
                new UpdateSpeakerProfileCommand(
                    payload.DisplayName,
                    payload.Biography,
                    payload.ConferenceOperationsOptIn),
                ifMatch,
                cancellationToken);
            return await WriteProfileAsync(request, updated, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProfileNotFound",
                "Your profile could not be found.",
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidProfile",
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
            "Sign in to manage your profile.",
            cancellationToken);

    private static async Task<HttpResponseData> WriteProfileAsync(
        HttpRequestData request,
        Versioned<SpeakerProfile> versioned,
        CancellationToken cancellationToken)
    {
        var profile = versioned.Value;
        var response = await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new SpeakerProfileDto
            {
                UserId = profile.UserId,
                DisplayName = profile.DisplayName,
                Biography = profile.Biography,
                Email = profile.Email,
                EmailVerified = profile.EmailVerified,
                ConferenceOperationsOptIn = profile.ConferenceOperationsOptIn,
                ETag = versioned.ETag,
                EmailSuppressed = profile.EmailSuppressed
            },
            cancellationToken);
        response.Headers.Add("ETag", versioned.ETag);
        return response;
    }

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
