using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;
using Cfp.Domain.Reviews;

namespace Cfp.Application.Proposals;

public sealed record ManagedProposalPage(ProposalPage Page);

public sealed record ManagedProposalDetails(
    Versioned<Proposal> Proposal,
    IReadOnlyList<Versioned<Review>> Reviews);

public sealed class ListConferenceProposalsHandler(
    ConferenceAuthorizationService authorization,
    IProposalWorkflowStore proposals)
{
    public async Task<ManagedProposalPage> HandleAsync(
        string conferenceId,
        Actor actor,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        return new ManagedProposalPage(await proposals.ListByConferenceAsync(
            conferenceId,
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            cancellationToken));
    }
}

public sealed class GetConferenceProposalHandler(
    ConferenceAuthorizationService authorization,
    IProposalWorkflowStore proposals,
    IReviewWorkflowStore reviews)
{
    public async Task<ManagedProposalDetails> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        var proposal = await proposals.GetByIdAsync(conferenceId, proposalId, cancellationToken)
            ?? throw new KeyNotFoundException("Proposal was not found.");
        var proposalReviews = await reviews.ListReviewsForProposalAsync(
            conferenceId,
            proposalId,
            cancellationToken);
        return new ManagedProposalDetails(proposal, proposalReviews);
    }
}
