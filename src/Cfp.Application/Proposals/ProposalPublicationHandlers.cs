using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.PublicConferences;
using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;

namespace Cfp.Application.Proposals;

public sealed record PublicProposal(
    string Id,
    string ConferenceTitle,
    string Title,
    string Abstract,
    string Speakers);

public sealed record PublicProposalPage(
    string ConferenceTitle,
    IReadOnlyList<PublicProposal> Proposals,
    string? ContinuationToken);

public sealed class SetProposalPublicationConsentHandler(IProposalWorkflowStore proposals)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        bool confirmed,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var current = await proposals.GetOwnedAsync(
            conferenceId,
            proposalId,
            actor.UserId,
            cancellationToken) ?? throw new KeyNotFoundException("Proposal was not found.");
        var updated = current.Value.SetPublicationConsent(
            actor.UserId,
            confirmed,
            confirmed ? "public-proposal-consent-v1" : null,
            nowUtc);
        var audit = new AuditEvent(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actor.UserId,
            confirmed ? "ProposalPublicationConsentConfirmed" : "ProposalPublicationConsentRevoked",
            proposalId,
            nowUtc.ToUniversalTime(),
            confirmed
                ? "Speaker confirmed all co-speakers consented to public publication."
                : "Speaker revoked public publication consent.");
        return await proposals.SetPublicationConsentAsync(updated, expectedEtag, audit, cancellationToken);
    }
}

public sealed class SetProposalPublicationStateHandler(
    ConferenceAuthorizationService authorization,
    IProposalWorkflowStore proposals)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        bool published,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        var current = await proposals.GetByIdAsync(conferenceId, proposalId, cancellationToken)
            ?? throw new KeyNotFoundException("Proposal was not found.");
        var updated = current.Value.SetPublicationState(published, nowUtc);
        var audit = new AuditEvent(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actor.UserId,
            published ? "ProposalPublished" : "ProposalHidden",
            proposalId,
            nowUtc.ToUniversalTime(),
            published ? "Accepted proposal made public." : "Proposal removed from public pages.");
        return await proposals.SetPublicationStateAsync(updated, expectedEtag, audit, cancellationToken);
    }
}

public sealed class ListPublicProposalsHandler(
    IPublicConferenceReader conferences,
    IProposalWorkflowStore proposals)
{
    public async Task<PublicProposalPage?> HandleAsync(
        string slug,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var conference = await conferences.GetBySlugAsync(slug, cancellationToken);
        if (conference is null)
        {
            return null;
        }

        if (!conference.PublicShowcaseEnabled)
        {
            return new PublicProposalPage(conference.Title, [], null);
        }

        var page = await proposals.ListByConferenceAsync(
            conference.Id,
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            cancellationToken);
        var publicProposals = page.Proposals
            .Select(item => item.Value)
            .Where(proposal =>
                proposal.Status == ProposalStatus.Accepted &&
                proposal.PublicationConsentConfirmed &&
                proposal.PublicationState == ProposalPublicationState.Published)
            .Select(proposal => ToPublicProposal(proposal, conference.Title))
            .ToArray();
        return new PublicProposalPage(conference.Title, publicProposals, page.ContinuationToken);
    }

    private static PublicProposal ToPublicProposal(Proposal proposal, string conferenceTitle)
    {
        proposal.Answers.TryGetValue("title", out var title);
        proposal.Answers.TryGetValue("abstract", out var abstractText);
        proposal.Answers.TryGetValue("speakers", out var speakers);
        return new PublicProposal(proposal.Id, conferenceTitle, title ?? string.Empty, abstractText ?? string.Empty, speakers ?? string.Empty);
    }
}

public sealed class GetPublicProposalHandler(
    IPublicConferenceReader conferences,
    IProposalWorkflowStore proposals)
{
    public async Task<PublicProposal?> HandleAsync(
        string slug,
        string proposalId,
        CancellationToken cancellationToken)
    {
        var conference = await conferences.GetBySlugAsync(slug, cancellationToken);
        if (conference is null)
        {
            return null;
        }

        var proposal = await proposals.GetByIdAsync(conference.Id, proposalId, cancellationToken);
        if (proposal is null ||
            proposal.Value.Status != ProposalStatus.Accepted ||
            !proposal.Value.PublicationConsentConfirmed ||
            proposal.Value.PublicationState != ProposalPublicationState.Published)
        {
            return null;
        }

        return new PublicProposal(
            proposal.Value.Id,
            conference.Title,
            proposal.Value.Answers.GetValueOrDefault("title", string.Empty),
            proposal.Value.Answers.GetValueOrDefault("abstract", string.Empty),
            proposal.Value.Answers.GetValueOrDefault("speakers", string.Empty));
    }
}
