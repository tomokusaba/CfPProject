using Cfp.Application.Abstractions;
using Cfp.Application.Notifications;
using Cfp.Domain.Notifications;

namespace Cfp.Tests.Application;

public sealed class ProfileEmailRecipientPolicyTests
{
    [Fact]
    public async Task TransactionalEmail_RequiresConfirmedUnsuppressedAddressButNotOptIn()
    {
        var profile = new SpeakerProfile(
            "user-1",
            "Speaker",
            string.Empty,
            "speaker@example.test",
            EmailVerified: true,
            ConferenceOperationsOptIn: false,
            EmailSuppressed: false,
            EmailSuppressionReason: null);
        var policy = new ProfileEmailRecipientPolicy(new FakeProfileStore(profile));

        var transactional = await policy.ResolveAsync(
            "user-1",
            EmailCategory.Transactional,
            CancellationToken.None);
        var conferenceOperations = await policy.ResolveAsync(
            "user-1",
            EmailCategory.ConferenceOperations,
            CancellationToken.None);

        Assert.True(transactional.IsAllowed);
        Assert.Equal("speaker@example.test", transactional.EmailAddress);
        Assert.False(conferenceOperations.IsAllowed);
    }

    [Fact]
    public async Task SuppressedAddress_IsDeniedEvenForTransactionalMessages()
    {
        var profile = new SpeakerProfile(
            "user-1",
            "Speaker",
            string.Empty,
            "speaker@example.test",
            EmailVerified: true,
            ConferenceOperationsOptIn: true,
            EmailSuppressed: true,
            EmailSuppressionReason: "hard bounce");
        var policy = new ProfileEmailRecipientPolicy(new FakeProfileStore(profile));

        var result = await policy.ResolveAsync(
            "user-1",
            EmailCategory.Transactional,
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Null(result.EmailAddress);
    }

    private sealed class FakeProfileStore(SpeakerProfile profile) : IUserProfileStore
    {
        public Task<Versioned<SpeakerProfile>?> GetAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<Versioned<SpeakerProfile>?>(new Versioned<SpeakerProfile>(profile, "\"etag\""));

        public Task<Versioned<SpeakerProfile>> UpdateAsync(
            SpeakerProfile updated,
            string expectedEtag,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Versioned<SpeakerProfile>(updated, expectedEtag));

        public Task SetEmailSuppressedAsync(
            string userId,
            bool suppressed,
            string reason,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
