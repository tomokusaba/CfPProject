using Cfp.Domain.Reviews;

namespace Cfp.Tests.Domain;

public sealed class ReviewTests
{
    [Fact]
    public void Submit_RejectsConflictedReviewerAndOutOfRangeScore()
    {
        var conflictedAssignment = new ReviewerAssignment(
            "conf-1",
            "proposal-1",
            "reviewer-1",
            DateTimeOffset.UtcNow,
            HasConflictOfInterest: true);

        Assert.Throws<InvalidOperationException>(() => Review.Submit(
            conflictedAssignment,
            4,
            5,
            "Useful feedback.",
            DateTimeOffset.UtcNow));

        var assignment = conflictedAssignment with { HasConflictOfInterest = false };
        Assert.Throws<ArgumentOutOfRangeException>(() => Review.Submit(
            assignment,
            6,
            5,
            "Useful feedback.",
            DateTimeOffset.UtcNow));
    }
}
