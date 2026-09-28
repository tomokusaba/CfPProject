using Cfp.Application.Auditing;
using Cfp.Domain.Proposals;

namespace Cfp.Application.Abstractions;

public sealed record ProposalPage(
    IReadOnlyList<Versioned<Proposal>> Proposals,
    string? ContinuationToken);

public interface IProposalWorkflowStore
{
    Task<Versioned<Proposal>> CreateDraftAsync(
        Proposal proposal,
        AuditEvent auditEvent,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>?> GetOwnedAsync(
        string conferenceId,
        string proposalId,
        string ownerUserId,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>?> GetByIdAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>> UpdateAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>> SubmitAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>> WithdrawAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>> SetPublicationConsentAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>> SetPublicationStateAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<ProposalPage> ListByOwnerAsync(
        string ownerUserId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);

    Task<ProposalPage> ListByConferenceAsync(
        string conferenceId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);
}
