using Cfp.Application.Abstractions;
using Cfp.Application.Identity;

namespace Cfp.Application.Profiles;

public sealed class GetSpeakerProfileHandler(IUserProfileStore profiles)
{
    public async Task<Versioned<SpeakerProfile>> HandleAsync(
        Actor actor,
        CancellationToken cancellationToken) =>
        await profiles.GetAsync(actor.UserId, cancellationToken)
        ?? throw new KeyNotFoundException("Profile was not found.");
}

public sealed class UpdateSpeakerProfileHandler(IUserProfileStore profiles)
{
    public async Task<Versioned<SpeakerProfile>> HandleAsync(
        Actor actor,
        UpdateSpeakerProfileCommand command,
        string expectedEtag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 80)
        {
            throw new ArgumentException("Display name is required and must be at most 80 characters.");
        }

        if (command.Biography.Length > 5_000)
        {
            throw new ArgumentException("Biography must be at most 5,000 characters.");
        }

        var current = await profiles.GetAsync(actor.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Profile was not found.");
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The profile changed. Reload it before saving.");
        }

        var updated = current.Value with
        {
            DisplayName = command.DisplayName.Trim(),
            Biography = command.Biography.Trim(),
            ConferenceOperationsOptIn = command.ConferenceOperationsOptIn
        };
        return await profiles.UpdateAsync(updated, expectedEtag, cancellationToken);
    }
}
