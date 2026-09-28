using Cfp.Application.Auditing;
using Cfp.Domain.Proposals;
using Cfp.Domain.Reviews;

namespace Cfp.Application.Abstractions;

public sealed record ReviewTask(
    ReviewerAssignment Assignment,
    Proposal Proposal,
    Review? Review,
    string ProposalETag,
    string? ReviewETag);

public sealed record ReviewTaskPage(
    IReadOnlyList<ReviewTask> Tasks,
    string? ContinuationToken);

public interface IReviewWorkflowStore
{
    Task<Versioned<Proposal>?> GetProposalAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken);

    Task<Versioned<ReviewerAssignment>?> GetAssignmentAsync(
        string conferenceId,
        string proposalId,
        string reviewerUserId,
        CancellationToken cancellationToken);

    Task<ReviewerAssignment> AssignAsync(
        ReviewerAssignment assignment,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken);

    Task<ReviewerAssignment> DeclareConflictAsync(
        ReviewerAssignment assignment,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken);

    Task<Versioned<Review>> SaveReviewAsync(
        Review review,
        string? expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Versioned<Review>>> ListReviewsForProposalAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken);

    Task<Versioned<Proposal>> DecideAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<ReviewTaskPage> ListMyAssignmentsAsync(
        string reviewerUserId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);
}
