namespace Cfp.Domain.Conferences;

public enum ConferenceRole
{
    ConferenceOwner,
    Organizer,
    Reviewer,
    Speaker
}

public sealed record ConferenceMembership(
    string ConferenceId,
    string UserId,
    ConferenceRole Role,
    bool IsActive);
