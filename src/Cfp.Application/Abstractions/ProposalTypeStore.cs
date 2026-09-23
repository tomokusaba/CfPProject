using Cfp.Application.Auditing;
using Cfp.Domain.Proposals;

namespace Cfp.Application.Abstractions;

public sealed record Versioned<T>(T Value, string ETag);

public interface IProposalTypeStore
{
    Task<Versioned<ProposalType>?> GetAsync(
        string conferenceId,
        string proposalTypeId,
        CancellationToken cancellationToken);

    Task<ProposalType?> GetVersionAsync(
        string conferenceId,
        string proposalTypeId,
        int formVersion,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<ProposalType>>> ListAsync(
        string conferenceId,
        CancellationToken cancellationToken);

    Task<Versioned<ProposalType>> SaveNewVersionAsync(
        ProposalType proposalType,
        string? expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
