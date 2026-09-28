using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Abstractions;
using Cfp.Application.Identity;
using Cfp.Application.Proposals;
using Cfp.Contracts.V1;
using Cfp.Domain.Proposals;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class SpeakerProposalFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    SaveProposalDraftHandler createDraftHandler,
    UpdateProposalHandler updateHandler,
    SubmitProposalHandler submitHandler,
    WithdrawProposalHandler withdrawHandler,
    ListMyProposalsHandler listHandler,
    GetMyProposalHandler getHandler,
    IProposalTypeStore proposalTypes,
    SetProposalPublicationConsentHandler publicationConsentHandler,
    IConferenceLifecycleStore conferenceStore)
{
    [Function(nameof(ListMyProposals))]
    public async Task<HttpResponseData> ListMyProposals(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/me/proposals")]
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
        if (query["pageSize"] is { } value &&
            (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) ||
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
            cancellationToken);
        return await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new MyProposalsDto
            {
                Proposals = page.Proposals.Select(item => ToDto(item, [], null)).ToList(),
                ContinuationToken = page.ContinuationToken
            },
            cancellationToken);
    }

    [Function(nameof(GetMyProposal))]
    public async Task<HttpResponseData> GetMyProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/me/conferences/{conferenceId}/proposals/{proposalId}")]
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

        var proposal = await getHandler.HandleAsync(
            conferenceId,
            proposalId,
            actor,
            cancellationToken);
        return proposal is null
            ? await WriteExpectedErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "The proposal was not found.",
                cancellationToken)
            : await WriteProposalAsync(request, proposal, HttpStatusCode.OK, cancellationToken);
    }

    [Function(nameof(SetMyProposalPublicationConsent))]
    public async Task<HttpResponseData> SetMyProposalPublicationConsent(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/me/conferences/{conferenceId}/proposals/{proposalId}/public-consent")]
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

        SetProposalPublicationConsentRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SetProposalPublicationConsentRequestDto>(
                request,
                cancellationToken);
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
            var updated = await publicationConsentHandler.HandleAsync(
                conferenceId,
                proposalId,
                actor,
                payload.Confirmed,
                ifMatch,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await WriteProposalAsync(request, updated, HttpStatusCode.OK, cancellationToken);
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
        catch (UnauthorizedAccessException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "The proposal was not found.",
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidPublicationConsent",
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

    [Function(nameof(RevokeMyProposalPublicationConsent))]
    public async Task<HttpResponseData> RevokeMyProposalPublicationConsent(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "delete",
            Route = "v1/me/conferences/{conferenceId}/proposals/{proposalId}/public-consent")]
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

        try
        {
            var updated = await publicationConsentHandler.HandleAsync(
                conferenceId,
                proposalId,
                actor,
                confirmed: false,
                ifMatch,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await WriteProposalAsync(request, updated, HttpStatusCode.OK, cancellationToken);
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
        catch (UnauthorizedAccessException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "The proposal was not found.",
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

    [Function(nameof(CreateProposalDraft))]
    public async Task<HttpResponseData> CreateProposalDraft(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/me/conferences/{conferenceId}/proposals")]
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

        var idempotencyKey = GetSingleHeader(request, "Idempotency-Key");
        if (!Guid.TryParse(idempotencyKey, out var operationId))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "IdempotencyKeyRequired",
                "A GUID Idempotency-Key header is required.",
                cancellationToken);
        }

        ProposalDraftRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<ProposalDraftRequestDto>(request, cancellationToken);
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

        if (string.IsNullOrWhiteSpace(payload.ProposalTypeId) || payload.Answers is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidProposal",
                "Proposal type and answers are required.",
                cancellationToken);
        }

        try
        {
            var created = await createDraftHandler.HandleAsync(
                new SaveProposalDraftCommand(conferenceId, payload.ProposalTypeId, payload.Answers),
                actor,
                operationId.ToString("D"),
                DateTimeOffset.UtcNow,
                cancellationToken);
            var response = await WriteProposalAsync(
                request,
                created,
                HttpStatusCode.Created,
                cancellationToken);
            response.Headers.Add(
                "Location",
                $"/api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(created.Value.Id)}");
            return response;
        }
        catch (KeyNotFoundException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.NotFound, "NotFound", exception.Message, cancellationToken);
        }
        catch (ProposalValidationException exception)
        {
            return await WriteExpectedErrorAsync(
                request,
                HttpStatusCode.UnprocessableEntity,
                "InvalidProposal",
                string.Join(" ", exception.Errors),
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.Conflict, "ProposalConflict", exception.Message, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.BadRequest, "InvalidProposal", exception.Message, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.Conflict, "CfpClosed", exception.Message, cancellationToken);
        }
    }

    [Function(nameof(UpdateMyProposal))]
    public Task<HttpResponseData> UpdateMyProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "v1/me/conferences/{conferenceId}/proposals/{proposalId}")]
        HttpRequestData request,
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken) =>
        MutateProposalAsync(request, conferenceId, proposalId, cancellationToken, "update");

    [Function(nameof(SubmitMyProposal))]
    public Task<HttpResponseData> SubmitMyProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/me/conferences/{conferenceId}/proposals/{proposalId}/submit")]
        HttpRequestData request,
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken) =>
        MutateProposalAsync(request, conferenceId, proposalId, cancellationToken, "submit");

    [Function(nameof(WithdrawMyProposal))]
    public Task<HttpResponseData> WithdrawMyProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/me/conferences/{conferenceId}/proposals/{proposalId}/withdraw")]
        HttpRequestData request,
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken) =>
        MutateProposalAsync(request, conferenceId, proposalId, cancellationToken, "withdraw");

    private async Task<HttpResponseData> MutateProposalAsync(
        HttpRequestData request,
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken,
        string operation)
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
                "Send the current proposal ETag in the If-Match header.",
                cancellationToken);
        }

        IReadOnlyDictionary<string, string>? answers = null;
        if (operation == "update")
        {
            UpdateProposalRequestDto payload;
            try
            {
                payload = await MemoryPackHttp.ReadAsync<UpdateProposalRequestDto>(request, cancellationToken);
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

            answers = payload.Answers;
            if (answers is null)
            {
                return await MemoryPackHttp.WriteErrorAsync(
                    request,
                    HttpStatusCode.BadRequest,
                    "InvalidProposal",
                    "Answers are required.",
                    cancellationToken);
            }
        }

        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var saved = operation switch
            {
                "update" => await updateHandler.HandleAsync(
                    conferenceId,
                    proposalId,
                    actor,
                    answers!,
                    expectedEtag,
                    nowUtc,
                    cancellationToken),
                "submit" => await submitHandler.HandleAsync(
                    conferenceId,
                    proposalId,
                    actor,
                    expectedEtag,
                    nowUtc,
                    cancellationToken),
                _ => await withdrawHandler.HandleAsync(
                    conferenceId,
                    proposalId,
                    actor,
                    expectedEtag,
                    nowUtc,
                    cancellationToken)
            };

            return await WriteProposalAsync(request, saved, HttpStatusCode.OK, cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.NotFound, "ProposalNotFound", exception.Message, cancellationToken);
        }
        catch (ProposalValidationException exception)
        {
            return await WriteExpectedErrorAsync(
                request,
                HttpStatusCode.UnprocessableEntity,
                "InvalidProposal",
                string.Join(" ", exception.Errors),
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return await WriteExpectedErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "The proposal was not found.",
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.PreconditionFailed, "VersionConflict", exception.Message, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await WriteExpectedErrorAsync(request, HttpStatusCode.Conflict, "ProposalStateConflict", exception.Message, cancellationToken);
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
            "Sign in to access your proposals.",
            cancellationToken);

    private async Task<HttpResponseData> WriteProposalAsync(
        HttpRequestData request,
        Versioned<Proposal> versioned,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        var form = await proposalTypes.GetVersionAsync(
            versioned.Value.ConferenceId,
            versioned.Value.ProposalTypeId,
            versioned.Value.FormVersion,
            cancellationToken);
        if (form is null)
        {
            throw new InvalidOperationException("The proposal's saved form version is unavailable.");
        }

        var conference = await conferenceStore.GetAsync(versioned.Value.ConferenceId, cancellationToken);
        if (conference is null)
        {
            throw new InvalidOperationException("The proposal conference is unavailable.");
        }

        var response = await MemoryPackHttp.WriteAsync(
            request,
            statusCode,
            ToDto(versioned, form.Fields.Select(field => new ProposalFormFieldDto
            {
                Id = field.Id,
                Label = field.Label,
                Kind = field.Kind.ToString(),
                IsRequired = field.IsRequired,
                MaximumLength = field.MaximumLength,
                Options = field.Options?.ToList()
            }).ToList(), conference.Value),
            cancellationToken);
        response.Headers.Add("ETag", versioned.ETag);
        return response;
    }

    private static ProposalDto ToDto(
        Versioned<Proposal> versioned,
        List<ProposalFormFieldDto> formFields,
        Cfp.Domain.Conferences.Conference? conference)
    {
        var proposal = versioned.Value;
        return new ProposalDto
        {
            Id = proposal.Id,
            ConferenceId = proposal.ConferenceId,
            ProposalTypeId = proposal.ProposalTypeId,
            FormVersion = proposal.FormVersion,
            Answers = new Dictionary<string, string>(proposal.Answers, StringComparer.Ordinal),
            Status = proposal.Status.ToString(),
            SubmittedAtUtc = proposal.SubmittedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
            UpdatedAtUtc = proposal.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ETag = versioned.ETag,
            FormFields = formFields,
            PublicationConsentConfirmed = proposal.PublicationConsentConfirmed,
            PublicationState = proposal.PublicationState.ToString(),
            ConferenceSlug = conference?.Slug ?? string.Empty,
            ConferenceTitle = conference?.Title ?? string.Empty
        };
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

    private static Task<HttpResponseData> WriteExpectedErrorAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(request, statusCode, code, message, cancellationToken);
}
