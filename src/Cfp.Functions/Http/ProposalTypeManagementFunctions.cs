using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.ProposalTypes;
using Cfp.Contracts.V1;
using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class ProposalTypeManagementFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ConferenceAuthorizationService authorization,
    ListManagedProposalTypesHandler listHandler,
    SaveProposalTypeHandler saveHandler)
{
    [Function(nameof(ListManagedProposalTypes))]
    public async Task<HttpResponseData> ListManagedProposalTypes(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/proposal-types")]
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
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Unauthorized,
                "AuthenticationRequired",
                "Sign in to manage proposal types.",
                cancellationToken);
        }

        try
        {
            await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
            var versions = await listHandler.HandleAsync(conferenceId, cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new ManagedProposalTypesDto
                {
                    ProposalTypes = versions.Select(ToManagedDto).ToList()
                },
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                "ConferenceRoleRequired",
                "You do not have permission to manage this conference.",
                cancellationToken);
        }
    }

    [Function(nameof(SaveProposalType))]
    public async Task<HttpResponseData> SaveProposalType(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/proposal-types")]
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
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Unauthorized,
                "AuthenticationRequired",
                "Sign in to manage proposal types.",
                cancellationToken);
        }

        try
        {
            await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
        }
        catch (ConferenceAuthorizationException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                "ConferenceRoleRequired",
                "You do not have permission to manage this conference.",
                cancellationToken);
        }

        SaveProposalTypeRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SaveProposalTypeRequestDto>(request, cancellationToken);
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

        if (payload.Name is null || payload.Description is null || payload.Fields is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidProposalType",
                "Proposal type name, description, and fields are required.",
                cancellationToken);
        }

        var isUpdate = !string.IsNullOrWhiteSpace(payload.ProposalTypeId);
        var expectedEtag = GetSingleHeader(request, "If-Match");
        if (isUpdate && string.IsNullOrWhiteSpace(expectedEtag))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionRequired,
                "IfMatchRequired",
                "Send the current ETag in the If-Match header.",
                cancellationToken);
        }

        if (!isUpdate && expectedEtag is not null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "UnexpectedIfMatch",
                "If-Match cannot be sent when creating a proposal type.",
                cancellationToken);
        }

        var fields = new List<ProposalFormField>();
        foreach (var field in payload.Fields)
        {
            if (field is null ||
                field.Id is null ||
                field.Label is null ||
                !Enum.TryParse<FormFieldKind>(field.Kind, ignoreCase: false, out var fieldKind))
            {
                return await MemoryPackHttp.WriteErrorAsync(
                    request,
                    HttpStatusCode.BadRequest,
                    "InvalidProposalForm",
                    "Each form field must contain a valid ID, label, and field kind.",
                    cancellationToken);
            }

            fields.Add(new ProposalFormField(
                field.Id,
                field.Label,
                fieldKind,
                field.IsRequired,
                field.MaximumLength,
                field.Options));
        }

        try
        {
            var saved = await saveHandler.HandleAsync(
                conferenceId,
                new SaveProposalTypeCommand(
                    payload.ProposalTypeId,
                    payload.Name,
                    payload.Description,
                    payload.DurationMinutes,
                    payload.IsAcceptingSubmissions,
                    payload.IsPublic,
                    fields),
                actor,
                expectedEtag,
                DateTimeOffset.UtcNow,
                cancellationToken);

            var response = await MemoryPackHttp.WriteAsync(
                request,
                isUpdate ? HttpStatusCode.OK : HttpStatusCode.Created,
                new SavedProposalTypeDto
                {
                    Id = saved.Value.Id,
                    Name = saved.Value.Name,
                    FormVersion = saved.Value.FormVersion
                },
                cancellationToken);
            response.Headers.Add("ETag", saved.ETag);
            return response;
        }
        catch (ConferenceAuthorizationException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                "ConferenceRoleRequired",
                "You do not have permission to manage this conference.",
                cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalTypeNotFound",
                "The proposal type was not found.",
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
        catch (ProposalDefinitionValidationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.UnprocessableEntity,
                "InvalidProposalType",
                string.Join(" ", exception.Errors),
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidProposalType",
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

    private static string? GetSingleHeader(HttpRequestData request, string name)
    {
        if (!request.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var headers = values.ToArray();
        return headers.Length == 1 ? headers[0] : null;
    }

    private static ManagedProposalTypeDto ToManagedDto(Versioned<ProposalType> versioned) => new()
    {
        Id = versioned.Value.Id,
        Name = versioned.Value.Name,
        Description = versioned.Value.Description,
        DurationMinutes = versioned.Value.DurationMinutes,
        FormVersion = versioned.Value.FormVersion,
        IsAcceptingSubmissions = versioned.Value.IsAcceptingSubmissions,
        IsPublic = versioned.Value.IsPublic,
        ETag = versioned.ETag,
        Fields = versioned.Value.Fields.Select(field => new ProposalFormFieldDto
        {
            Id = field.Id,
            Label = field.Label,
            Kind = field.Kind.ToString(),
            IsRequired = field.IsRequired,
            MaximumLength = field.MaximumLength,
            Options = field.Options?.ToList()
        }).ToList()
    };
}
