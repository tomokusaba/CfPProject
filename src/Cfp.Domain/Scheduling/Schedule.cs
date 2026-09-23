namespace Cfp.Domain.Scheduling;

public sealed record ScheduleSlot(
    string Id,
    string ProposalId,
    string RoomId,
    string? TrackId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int ProposalDurationMinutes);

public sealed record ScheduleRoom(string Id, string Name);

public sealed record ScheduleTrack(string Id, string Name);

public sealed record SchedulePlan(
    string ConferenceId,
    int Revision,
    IReadOnlyList<ScheduleRoom> Rooms,
    IReadOnlyList<ScheduleTrack> Tracks,
    IReadOnlyList<ScheduleSlot> Slots,
    DateTimeOffset UpdatedAtUtc);

public static class ScheduleConflictPolicy
{
    public static IReadOnlyList<string> Validate(SchedulePlan plan)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(plan.ConferenceId))
        {
            errors.Add("Conference ID is required.");
        }

        if (plan.Revision < 1)
        {
            errors.Add("Schedule revision must be positive.");
        }

        if (plan.Rooms.Count > 100 || plan.Tracks.Count > 100 || plan.Slots.Count > 500)
        {
            errors.Add("Schedule size exceeds the supported limit.");
        }

        if (plan.Rooms.Any(room => string.IsNullOrWhiteSpace(room.Id) || string.IsNullOrWhiteSpace(room.Name)) ||
            plan.Rooms.Select(room => room.Id).Distinct(StringComparer.Ordinal).Count() != plan.Rooms.Count)
        {
            errors.Add("Room IDs and names must be non-empty, and room IDs must be unique.");
        }

        if (plan.Tracks.Any(track => string.IsNullOrWhiteSpace(track.Id) || string.IsNullOrWhiteSpace(track.Name)) ||
            plan.Tracks.Select(track => track.Id).Distinct(StringComparer.Ordinal).Count() != plan.Tracks.Count)
        {
            errors.Add("Track IDs and names must be non-empty, and track IDs must be unique.");
        }

        var roomIds = plan.Rooms.Select(room => room.Id).ToHashSet(StringComparer.Ordinal);
        var trackIds = plan.Tracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var slot in plan.Slots)
        {
            if (!roomIds.Contains(slot.RoomId))
            {
                errors.Add($"Slot {slot.Id} references an unknown room.");
            }

            if (slot.TrackId is not null && !trackIds.Contains(slot.TrackId))
            {
                errors.Add($"Slot {slot.Id} references an unknown track.");
            }
        }

        errors.AddRange(Validate(plan.Slots));
        return errors;
    }

    public static IReadOnlyList<string> Validate(IReadOnlyList<ScheduleSlot> slots)
    {
        var errors = new List<string>();

        foreach (var slot in slots)
        {
            if (slot.EndsAtUtc <= slot.StartsAtUtc)
            {
                errors.Add($"Slot {slot.Id} must end after it starts.");
            }
            else if ((slot.EndsAtUtc - slot.StartsAtUtc).TotalMinutes != slot.ProposalDurationMinutes)
            {
                errors.Add($"Slot {slot.Id} does not match its proposal duration.");
            }
        }

        foreach (var group in slots.GroupBy(slot => slot.RoomId, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(slot => slot.StartsAtUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].StartsAtUtc < ordered[index - 1].EndsAtUtc)
                {
                    errors.Add($"Room {group.Key} has overlapping slots {ordered[index - 1].Id} and {ordered[index].Id}.");
                }
            }
        }

        foreach (var duplicate in slots.GroupBy(slot => slot.ProposalId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Proposal {duplicate.Key} is scheduled more than once.");
        }

        return errors;
    }
}
