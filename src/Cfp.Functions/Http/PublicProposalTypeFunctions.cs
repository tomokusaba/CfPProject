using System.Net;
using Cfp.Application.ProposalTypes;
using Cfp.Contracts.V1;
using Cfp.Domain.Proposals;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class PublicProposalTypeFunctions(GetPublicProposalTypesHandler handler)
{
    [Function(nameof(GetPublicProposalTypes))]
    public async Task<HttpResponseData> GetPublicProposalTypes(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/public/conferences/{slug}/proposal-types")]
        HttpRequestData request,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var types = await handler.HandleAsync(slug, cancellationToken);
        if (types is null)
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
            new PublicProposalTypesDto
            {
                ProposalTypes = types.Select(ToDto).ToList()
            },
            cancellationToken);
    }

    private static PublicProposalTypeDto ToDto(ProposalType proposalType) => new()
    {
        Id = proposalType.Id,
        Name = proposalType.Name,
        Description = proposalType.Description,
        DurationMinutes = proposalType.DurationMinutes,
        FormVersion = proposalType.FormVersion,
        IsAcceptingSubmissions = proposalType.IsAcceptingSubmissions,
        Fields = proposalType.Fields.Select(field => new ProposalFormFieldDto
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
