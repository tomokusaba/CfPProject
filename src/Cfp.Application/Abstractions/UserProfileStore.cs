namespace Cfp.Application.Abstractions;

public sealed record SpeakerProfile(
    string UserId,
    string DisplayName,
    string Biography,
    string? Email,
    bool EmailVerified,
    bool ConferenceOperationsOptIn,
    bool EmailSuppressed,
    string? EmailSuppressionReason);

public sealed record UpdateSpeakerProfileCommand(
    string DisplayName,
    string Biography,
    bool ConferenceOperationsOptIn);

public interface IUserProfileStore
{
    Task<Versioned<SpeakerProfile>?> GetAsync(
        string userId,
        CancellationToken cancellationToken);

    Task<Versioned<SpeakerProfile>> UpdateAsync(
        SpeakerProfile profile,
        string expectedEtag,
        CancellationToken cancellationToken);

    Task SetEmailSuppressedAsync(
        string userId,
        bool suppressed,
        string reason,
        CancellationToken cancellationToken);
}
