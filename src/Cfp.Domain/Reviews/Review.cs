namespace Cfp.Domain.Reviews;

public sealed record ReviewerAssignment(
    string ConferenceId,
    string ProposalId,
    string ReviewerUserId,
    DateTimeOffset AssignedAtUtc,
    bool HasConflictOfInterest);

public sealed record Review(
    string ConferenceId,
    string ProposalId,
    string ReviewerUserId,
    int Score,
    string Comment,
    bool HasConflictOfInterest,
    DateTimeOffset SubmittedAtUtc)
{
    public static Review Submit(
        ReviewerAssignment assignment,
        int score,
        int maximumScore,
        string comment,
        DateTimeOffset nowUtc)
    {
        if (assignment.HasConflictOfInterest)
        {
            throw new InvalidOperationException("A reviewer with a conflict of interest cannot submit a review.");
        }

        if (score < 1 || score > maximumScore)
        {
            throw new ArgumentOutOfRangeException(nameof(score), $"Score must be between 1 and {maximumScore}.");
        }

        if (string.IsNullOrWhiteSpace(comment) || comment.Length > 10_000)
        {
            throw new ArgumentException("A review comment is required and must be at most 10,000 characters.", nameof(comment));
        }

        return new Review(
            assignment.ConferenceId,
            assignment.ProposalId,
            assignment.ReviewerUserId,
            score,
            comment.Trim(),
            false,
            nowUtc.ToUniversalTime());
    }
}
