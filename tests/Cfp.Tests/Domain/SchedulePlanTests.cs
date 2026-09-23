using Cfp.Domain.Scheduling;

namespace Cfp.Tests.Domain;

public sealed class SchedulePlanTests
{
    [Fact]
    public void Validate_RejectsUnknownRoomAndDuplicateRooms()
    {
        var startsAt = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
        var plan = new SchedulePlan(
            "conference-1",
            1,
            [new ScheduleRoom("room-a", "Room A"), new ScheduleRoom("room-a", "Duplicate room")],
            [],
            [new ScheduleSlot("slot-1", "proposal-1", "missing-room", null, startsAt, startsAt.AddMinutes(30), 30)],
            startsAt);

        var errors = ScheduleConflictPolicy.Validate(plan);

        Assert.Contains(errors, error => error.Contains("room IDs must be unique", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unknown room", StringComparison.Ordinal));
    }
}
