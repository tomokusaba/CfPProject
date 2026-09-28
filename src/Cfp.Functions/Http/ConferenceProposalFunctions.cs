using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.Proposals;
using Cfp.Contracts.V1;
using Cfp.Domain.Proposals;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class ConferenceProposalFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ListConferenceProposalsHandler listHandler,
    GetConferenceProposalHandler getHandler,
    SetProposalPublicationStateHandler publicationHandler)
{
    [Function(nameof(ListConferenceProposals))]
    public async Task<HttpResponseData> ListConferenceProposals(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/proposals")]
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

        try
        {
            var page = await listHandler.HandleAsync(
                conferenceId,
                actor,
                pageSize,
                continuationToken,
                cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ManagedProposalsDto
                {
                    Proposals = page.Page.Proposals.Select(ToDto).ToList(),
                    ContinuationToken = page.Page.ContinuationToken
                },
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
    }

    [Function(nameof(GetConferenceProposal))]
    public async Task<HttpResponseData> GetConferenceProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/proposals/{proposalId}")]
        HttpRequestData request,
        string conferenceId,
        string proposalId,
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
            var details = await getHandler.HandleAsync(
                conferenceId,
                proposalId,
                actor,
                cancellationToken);
            var dto = new ManagedProposalDetailsDto
            {
                Proposal = ToDto(details.Proposal),
                Reviews = details.Reviews.Select(item => new ReviewSummaryDto
                {
                    ReviewerUserId = item.Value.ReviewerUserId,
                    Score = item.Value.Score,
                    Comment = item.Value.Comment,
                    SubmittedAtUtc = item.Value.SubmittedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    ETag = item.ETag
                }).ToList()
            };
            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                dto,
                cancellationToken);
            response.Headers.Add("ETag", details.Proposal.ETag);
            return response;
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "The proposal was not found.",
                cancellationToken);
        }
    }

    [Function(nameof(SetProposalPublicationState))]
    public async Task<HttpResponseData> SetProposalPublicationState(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/manage/conferences/{conferenceId}/proposals/{proposalId}/publication")]
        HttpRequestData request,
        string conferenceId,
        string proposalId,
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
                "Send the current proposal ETag in If-Match.",
                cancellationToken);
        }

        SetProposalPublicationRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SetProposalPublicationRequestDto>(request, cancellationToken);
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

        try
        {
            var proposal = await publicationHandler.HandleAsync(
                conferenceId,
                proposalId,
                actor,
                payload.Published,
                ifMatch,
                DateTimeOffset.UtcNow,
                cancellationToken);
            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ManagedProposalDto
                {
                    Id = proposal.Value.Id,
                    ConferenceId = proposal.Value.ConferenceId,
                    ProposalTypeId = proposal.Value.ProposalTypeId,
                    FormVersion = proposal.Value.FormVersion,
                    Answers = new Dictionary<string, string>(proposal.Value.Answers, StringComparer.Ordinal),
                    Status = proposal.Value.Status.ToString(),
                    SubmittedAtUtc = proposal.Value.SubmittedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
                    DecisionReason = proposal.Value.DecisionReason,
                    ETag = proposal.ETag,
                    PublicationConsentConfirmed = proposal.Value.PublicationConsentConfirmed,
                    PublicationState = proposal.Value.PublicationState.ToString()
                },
                cancellationToken);
            response.Headers.Add("ETag", proposal.ETag);
            return response;
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "The proposal was not found.",
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
                "ProposalPublicationConflict",
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
            "Sign in to manage proposals.",
            cancellationToken);

    private static Task<HttpResponseData> ForbiddenAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Forbidden,
            "ConferenceRoleRequired",
            "You do not have permission to view proposals for this conference.",
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

    private static ManagedProposalDto ToDto(Versioned<Proposal> versioned) => new()
    {
        Id = versioned.Value.Id,
        ConferenceId = versioned.Value.ConferenceId,
        ProposalTypeId = versioned.Value.ProposalTypeId,
        FormVersion = versioned.Value.FormVersion,
        Answers = new Dictionary<string, string>(versioned.Value.Answers, StringComparer.Ordinal),
        Status = versioned.Value.Status.ToString(),
        SubmittedAtUtc = versioned.Value.SubmittedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
        DecisionReason = versioned.Value.DecisionReason,
        ETag = versioned.ETag,
        PublicationConsentConfirmed = versioned.Value.PublicationConsentConfirmed,
        PublicationState = versioned.Value.PublicationState.ToString()
    };
}
