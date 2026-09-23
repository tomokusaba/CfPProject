using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.Reviews;
using Cfp.Contracts.V1;
using Cfp.Domain.Conferences;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class ProposalReviewFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    AssignReviewersHandler assignHandler,
    ListMyReviewAssignmentsHandler listHandler,
    SubmitReviewHandler submitHandler,
    DecideProposalHandler decideHandler)
{
    [Function(nameof(AssignReviewer))]
    public async Task<HttpResponseData> AssignReviewer(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/reviews/assignments")]
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

        AssignReviewerRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<AssignReviewerRequestDto>(request, cancellationToken);
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

        if (payload.ProposalId is null || payload.ReviewerUserId is null || payload.Reason is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidAssignment",
                "Proposal ID, reviewer user ID, and reason are required.",
                cancellationToken);
        }

        try
        {
            var assignment = await assignHandler.HandleAsync(
                conferenceId,
                payload.ProposalId,
                payload.ReviewerUserId,
                payload.Reason,
                actor,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.Created,
                new ReviewSubmissionResponseDto
                {
                    ConflictDeclared = assignment.HasConflictOfInterest,
                    ProposalId = assignment.ProposalId
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
                "ProposalNotFound",
                exception.Message,
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "AssignmentConflict",
                exception.Message,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidAssignment",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "ProposalStateConflict",
                exception.Message,
                cancellationToken);
        }
    }

    [Function(nameof(ListMyReviewAssignments))]
    public async Task<HttpResponseData> ListMyReviewAssignments(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/me/review-assignments")]
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

        var page = await listHandler.HandleAsync(actor, pageSize, continuationToken, cancellationToken);
        return await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new ReviewTasksDto
            {
                Tasks = page.Tasks.Select(ToDto).ToList(),
                ContinuationToken = page.ContinuationToken
            },
            cancellationToken);
    }

    [Function(nameof(SubmitReview))]
    public async Task<HttpResponseData> SubmitReview(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/reviewer/conferences/{conferenceId}/proposals/{proposalId}/review")]
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

        ReviewSubmissionRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<ReviewSubmissionRequestDto>(request, cancellationToken);
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

        var etag = GetSingleHeader(request, "If-Match");
        try
        {
            var review = await submitHandler.HandleAsync(
                conferenceId,
                proposalId,
                actor,
                payload.Score,
                payload.Comment ?? string.Empty,
                payload.HasConflictOfInterest,
                etag,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ReviewSubmissionResponseDto
                {
                    ConflictDeclared = review is null,
                    ProposalId = proposalId,
                    Score = review?.Value.Score,
                    Comment = review?.Value.Comment,
                    ETag = review?.ETag
                },
                cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ReviewAssignmentNotFound",
                exception.Message,
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
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
                "InvalidReview",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "ReviewConflict",
                exception.Message,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                "ReviewerRequired",
                "Only the assigned reviewer can submit this review.",
                cancellationToken);
        }
    }

    [Function(nameof(DecideProposal))]
    public async Task<HttpResponseData> DecideProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/proposals/{proposalId}/decision")]
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
                "Send the current proposal ETag in the If-Match header.",
                cancellationToken);
        }

        ProposalDecisionRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<ProposalDecisionRequestDto>(request, cancellationToken);
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
            var decision = await decideHandler.HandleAsync(
                conferenceId,
                proposalId,
                actor,
                payload.Accepted,
                payload.Reason ?? string.Empty,
                ifMatch,
                DateTimeOffset.UtcNow,
                cancellationToken);
            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ProposalDecisionResponseDto
                {
                    ProposalId = decision.Value.Id,
                    Status = decision.Value.Status.ToString(),
                    DecisionReason = decision.Value.DecisionReason
                },
                cancellationToken);
            response.Headers.Add("ETag", decision.ETag);
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
                "ProposalNotFound",
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
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidDecision",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "ProposalStateConflict",
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
            "Sign in to continue.",
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

    private static ReviewTaskDto ToDto(ReviewTask task) => new()
    {
        ConferenceId = task.Assignment.ConferenceId,
        ProposalId = task.Assignment.ProposalId,
        Answers = new Dictionary<string, string>(task.Proposal.Answers, StringComparer.Ordinal),
        ProposalStatus = task.Proposal.Status.ToString(),
        HasConflictOfInterest = task.Assignment.HasConflictOfInterest,
        ReviewScore = task.Review?.Score,
        ReviewComment = task.Review?.Comment,
        ProposalETag = task.ProposalETag,
        ReviewETag = task.ReviewETag
    };

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
