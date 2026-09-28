using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Conferences;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Tests.Application;

public sealed class CreateConferenceHandlerTests
{
    [Fact]
    public async Task HandleAsync_CreatesPrivateDraftAndOwnerWithStableIdempotentIdentity()
    {
        var store = new RecordingConferenceManagementStore();
        var handler = new CreateConferenceHandler(store);
        var command = new CreateConferenceCommand(
            "systems-summit",
            "Systems Summit",
            "A conference about systems.",
            "Asia/Tokyo",
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var actor = new Actor("user-1");
        var now = DateTimeOffset.UtcNow;

        var created = await handler.HandleAsync(
            command,
            actor,
            "c094c219-7602-4d12-b27f-a9e80bb11ec9",
            now,
            CancellationToken.None);
        var repeated = await handler.HandleAsync(
            command,
            actor,
            "c094c219-7602-4d12-b27f-a9e80bb11ec9",
            now,
            CancellationToken.None);

        Assert.Equal(created.Id, repeated.Id);
        Assert.Equal(ConferenceLifecycleState.Draft, created.LifecycleState);
        Assert.Equal(ConferenceVisibility.Private, created.Visibility);
        Assert.Equal(ConferenceRole.ConferenceOwner, store.OwnerMembership?.Role);
        Assert.Equal("user-1", store.OwnerMembership?.UserId);
        Assert.Equal("ConferenceCreated", store.AuditOperation);
        Assert.Equal(store.RequestHash, store.SecondRequestHash);
    }

    [Fact]
    public async Task HandleAsync_RequiresGuidIdempotencyKey()
    {
        var handler = new CreateConferenceHandler(new RecordingConferenceManagementStore());
        var command = new CreateConferenceCommand(
            "systems-summit",
            "Systems Summit",
            string.Empty,
            "Asia/Tokyo",
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            command,
            new Actor("user-1"),
            "not-an-id",
            DateTimeOffset.UtcNow,
            CancellationToken.None));
    }

    private sealed class RecordingConferenceManagementStore : IConferenceManagementStore
    {
        private string? _lastIdempotencyKey;
        private Conference? _conference;

        public ConferenceMembership? OwnerMembership { get; private set; }
        public string? AuditOperation { get; private set; }
        public string? RequestHash { get; private set; }
        public string? SecondRequestHash { get; private set; }

        public Task<Conference> CreateAsync(
            Conference conference,
            ConferenceMembership ownerMembership,
            AuditEvent auditEvent,
            string idempotencyKey,
            string requestHash,
            CancellationToken cancellationToken)
        {
            if (_lastIdempotencyKey is null)
            {
                _lastIdempotencyKey = idempotencyKey;
                _conference = conference;
                OwnerMembership = ownerMembership;
                AuditOperation = auditEvent.Operation;
                RequestHash = requestHash;
            }
            else
            {
                SecondRequestHash = requestHash;
            }

            return Task.FromResult(_conference!);
        }
    }
}
