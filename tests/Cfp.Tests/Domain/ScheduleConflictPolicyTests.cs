using Cfp.Domain.Scheduling;

namespace Cfp.Tests.Domain;

public sealed class ScheduleConflictPolicyTests
{
    [Fact]
    public void Validate_FindsRoomOverlapAndDuplicateProposal()
    {
        var start = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
        var slots = new[]
        {
            new ScheduleSlot("slot-1", "proposal-1", "room-a", null, start, start.AddMinutes(30), 30),
            new ScheduleSlot("slot-2", "proposal-1", "room-a", null, start.AddMinutes(20), start.AddMinutes(50), 30)
        };

        var errors = ScheduleConflictPolicy.Validate(slots);

        Assert.Contains(errors, error => error.Contains("overlapping", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsAdjacentSlots()
    {
        var start = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
        var slots = new[]
        {
            new ScheduleSlot("slot-1", "proposal-1", "room-a", null, start, start.AddMinutes(30), 30),
            new ScheduleSlot("slot-2", "proposal-2", "room-a", null, start.AddMinutes(30), start.AddMinutes(60), 30)
        };

        Assert.Empty(ScheduleConflictPolicy.Validate(slots));
    }
}
