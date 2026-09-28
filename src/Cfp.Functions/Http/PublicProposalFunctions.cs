using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Proposals;
using Cfp.Contracts.V1;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class PublicProposalFunctions(
    ListPublicProposalsHandler listHandler,
    GetPublicProposalHandler getHandler)
{
    [Function(nameof(ListPublicProposals))]
    public async Task<HttpResponseData> ListPublicProposals(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/public/conferences/{slug}/proposals")]
        HttpRequestData request,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
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

        var page = await listHandler.HandleAsync(slug, pageSize, continuationToken, cancellationToken);
        if (page is null)
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
            new PublicProposalsDto
            {
                Proposals = page.Proposals.Select(proposal => new PublicProposalDto
                {
                    Id = proposal.Id,
                    Title = proposal.Title,
                    Abstract = proposal.Abstract,
                    Speakers = proposal.Speakers,
                    ConferenceTitle = page.ConferenceTitle
                }).ToList(),
                ContinuationToken = page.ContinuationToken
            },
            cancellationToken);
    }

    [Function(nameof(GetPublicProposal))]
    public async Task<HttpResponseData> GetPublicProposal(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/public/conferences/{slug}/proposals/{proposalId}")]
        HttpRequestData request,
        string slug,
        string proposalId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var proposal = await getHandler.HandleAsync(slug, proposalId, cancellationToken);
        if (proposal is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "ProposalNotFound",
                "This proposal is not publicly available.",
                cancellationToken);
        }

        return await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new PublicProposalDto
            {
                Id = proposal.Id,
                Title = proposal.Title,
                Abstract = proposal.Abstract,
                Speakers = proposal.Speakers,
                ConferenceTitle = proposal.ConferenceTitle
            },
            cancellationToken);
    }
}
