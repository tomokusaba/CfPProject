using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;
using Cfp.Domain.Reviews;

namespace Cfp.Application.Reviews;

public sealed class AssignReviewersHandler(
    ConferenceAuthorizationService authorization,
    IConferenceMembershipReader membershipReader,
    IReviewWorkflowStore reviewStore)
{
    public async Task<ReviewerAssignment> HandleAsync(
        string conferenceId,
        string proposalId,
        string reviewerUserId,
        string reason,
        Actor actor,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);

        if (string.IsNullOrWhiteSpace(reviewerUserId))
        {
            throw new ArgumentException("A reviewer user ID is required.", nameof(reviewerUserId));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A reason of at most 500 characters is required.", nameof(reason));
        }

        var reviewerMembership = await membershipReader.GetMembershipAsync(
            conferenceId,
            reviewerUserId,
            cancellationToken);
        if (reviewerMembership is null ||
            !reviewerMembership.IsActive ||
            reviewerMembership.Role != ConferenceRole.Reviewer)
        {
            throw new ArgumentException("The user is not an active reviewer for this conference.", nameof(reviewerUserId));
        }

        var proposal = await reviewStore.GetProposalAsync(conferenceId, proposalId, cancellationToken)
            ?? throw new KeyNotFoundException("Proposal was not found.");
        if (proposal.Value.Status is not (ProposalStatus.Submitted or ProposalStatus.UnderReview))
        {
            throw new InvalidOperationException("Only submitted proposals can be assigned for review.");
        }

        var assignment = new ReviewerAssignment(
            conferenceId,
            proposalId,
            reviewerUserId,
            nowUtc.ToUniversalTime(),
            HasConflictOfInterest: false);
        return await reviewStore.AssignAsync(assignment, actor.UserId, reason.Trim(), cancellationToken);
    }
}

public sealed class SubmitReviewHandler(
    IReviewWorkflowStore reviewStore,
    IConferenceMembershipReader membershipReader)
{
    public async Task<Versioned<Review>?> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        int score,
        string comment,
        bool hasConflictOfInterest,
        string? expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var membership = await membershipReader.GetMembershipAsync(
            conferenceId,
            actor.UserId,
            cancellationToken);
        if (membership is null || !membership.IsActive || membership.Role != ConferenceRole.Reviewer)
        {
            throw new ConferenceAuthorizationException();
        }

        var assignment = await reviewStore.GetAssignmentAsync(
            conferenceId,
            proposalId,
            actor.UserId,
            cancellationToken) ?? throw new KeyNotFoundException("Review assignment was not found.");

        if (hasConflictOfInterest)
        {
            await reviewStore.DeclareConflictAsync(
                assignment.Value,
                actor.UserId,
                "Reviewer declared a conflict of interest.",
                cancellationToken);
            return null;
        }

        if (assignment.Value.HasConflictOfInterest)
        {
            throw new InvalidOperationException("This assignment has a declared conflict of interest.");
        }

        var review = Review.Submit(
            assignment.Value,
            score,
            maximumScore: 5,
            comment,
            nowUtc);
        var auditEvent = NewAudit(
            conferenceId,
            actor.UserId,
            "ReviewSubmitted",
            proposalId,
            nowUtc,
            "Review submitted.");
        return await reviewStore.SaveReviewAsync(
            review,
            expectedEtag,
            auditEvent,
            cancellationToken);
    }

    private static AuditEvent NewAudit(
        string conferenceId,
        string actor,
        string operation,
        string target,
        DateTimeOffset nowUtc,
        string summary) =>
        new($"audit:{Guid.NewGuid():N}", conferenceId, actor, operation, target, nowUtc.ToUniversalTime(), summary);
}

public sealed class DecideProposalHandler(
    ConferenceAuthorizationService authorization,
    IReviewWorkflowStore reviewStore)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        bool accepted,
        string reason,
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
        var proposal = await reviewStore.GetProposalAsync(conferenceId, proposalId, cancellationToken)
            ?? throw new KeyNotFoundException("Proposal was not found.");
        var decided = proposal.Value.Decide(accepted, reason, actor.UserId, nowUtc);
        var auditEvent = new AuditEvent(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actor.UserId,
            accepted ? "ProposalAccepted" : "ProposalRejected",
            proposalId,
            nowUtc.ToUniversalTime(),
            $"Proposal decision recorded: {(accepted ? "Accepted" : "Rejected")}.");
        return await reviewStore.DecideAsync(
            decided,
            expectedEtag,
            auditEvent,
            cancellationToken);
    }
}

public sealed class ListMyReviewAssignmentsHandler(
    IReviewWorkflowStore reviewStore,
    IConferenceMembershipReader membershipReader)
{
    public async Task<ReviewTaskPage> HandleAsync(
        Actor actor,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var page = await reviewStore.ListMyAssignmentsAsync(
            actor.UserId,
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            cancellationToken);
        var tasks = new List<ReviewTask>();
        foreach (var task in page.Tasks)
        {
            var membership = await membershipReader.GetMembershipAsync(
                task.Assignment.ConferenceId,
                actor.UserId,
                cancellationToken);
            if (membership is { IsActive: true, Role: ConferenceRole.Reviewer })
            {
                tasks.Add(task);
            }
        }

        return new ReviewTaskPage(tasks, page.ContinuationToken);
    }
}
