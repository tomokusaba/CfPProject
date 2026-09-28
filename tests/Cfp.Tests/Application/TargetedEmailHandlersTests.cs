using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.Notifications;
using Cfp.Domain.Conferences;
using Cfp.Domain.Notifications;
using Cfp.Domain.Proposals;

namespace Cfp.Tests.Application;

public sealed class TargetedEmailHandlersTests
{
    [Fact]
    public async Task PreviewTargetedEmail_FixesUniqueEligibleRecipientsAndSelectedStatuses()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var proposals = new FakeProposalWorkflowStore(
        [
            CreateProposal("proposal-1", "speaker-1", ProposalStatus.Accepted),
            CreateProposal("proposal-2", "speaker-1", ProposalStatus.Accepted),
            CreateProposal("proposal-3", "speaker-2", ProposalStatus.Accepted),
            CreateProposal("proposal-5", "speaker-3", ProposalStatus.Accepted),
            CreateProposal("proposal-4", "speaker-1", ProposalStatus.Submitted)
        ]);
        var profiles = new FakeProfileStore(new Dictionary<string, SpeakerProfile>(StringComparer.Ordinal)
        {
            ["speaker-1"] = new SpeakerProfile(
                "speaker-1",
                "Speaker One",
                string.Empty,
                "one@example.test",
                true,
                true,
                false,
                null),
            ["speaker-2"] = new SpeakerProfile(
                "speaker-2",
                "Speaker Two",
                string.Empty,
                "two@example.test",
                true,
                false,
                false,
                null)
        });
        var campaignStore = new FakeCampaignStoreForPreview();
        var handler = new PreviewTargetedEmailHandler(
            new ConferenceAuthorizationService(new FakeMembershipReader()),
            proposals,
            new ProfileEmailRecipientPolicy(profiles),
            campaignStore);

        var result = await handler.HandleAsync(
            "conference-1",
            new PreviewTargetedEmailCommand(
                ["Accepted"],
                " Schedule update ",
                " The schedule is available. ",
                "sender@example.test"),
            new Actor("organizer-1"),
            Guid.NewGuid().ToString("D"),
            now,
            CancellationToken.None);

        Assert.Equal(["speaker-1"], result.Campaign.Value.RecipientUserIds);
        Assert.Equal([ProposalStatus.Accepted], result.Campaign.Value.TargetProposalStatuses);
        Assert.Equal(1, result.EligibleRecipientCount);
        Assert.Equal(2, result.ExcludedRecipientCount);
        Assert.Equal("Schedule update", result.Campaign.Value.Subject);
        Assert.Equal("The schedule is available.", result.Campaign.Value.PlainTextContent);
        Assert.Equal("sender@example.test", result.Campaign.Value.SenderAddress);
        Assert.Equal(result.Campaign.Value.Id, campaignStore.CreatedCampaign?.Id);
    }

    [Fact]
    public async Task SendTargetedEmail_ReplayingConfirmationDoesNotCreateDuplicateOutboxItems()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var campaign = new EmailCampaign(
            "campaign_01",
            "conference-1",
            "organizer-1",
            [Cfp.Domain.Proposals.ProposalStatus.Accepted],
            ["speaker-1"],
            "sender@example.test",
            "Conference update",
            "The schedule is available.",
            0,
            "request-hash",
            EmailCampaignState.Previewed,
            now,
            now.AddMinutes(30),
            null,
            null,
            null);
        var store = new FakeCampaignStore(new Versioned<EmailCampaign>(campaign, "\"preview-etag\""));
        var handler = new SendTargetedEmailHandler(
            new ConferenceAuthorizationService(new FakeMembershipReader()),
            store);
        var confirmationId = Guid.NewGuid().ToString("D");

        var first = await handler.HandleAsync(
            campaign.ConferenceId,
            campaign.Id,
            new Actor("organizer-1"),
            "Schedule announcement",
            confirmationId,
            "\"preview-etag\"",
            now.AddMinutes(1),
            CancellationToken.None);
        var replay = await handler.HandleAsync(
            campaign.ConferenceId,
            campaign.Id,
            new Actor("organizer-1"),
            "Schedule announcement",
            confirmationId,
            "\"preview-etag\"",
            now.AddMinutes(2),
            CancellationToken.None);

        Assert.Equal(EmailCampaignState.Ready, first.Value.State);
        Assert.Equal(first, replay);
        Assert.Single(store.EnqueuedItems);
        Assert.Equal("outbox:campaign_01:speaker-1", store.EnqueuedItems[0].Id);
        await Assert.ThrowsAsync<RequestConflictException>(() => handler.HandleAsync(
            campaign.ConferenceId,
            campaign.Id,
            new Actor("organizer-1"),
            "Different reason",
            confirmationId,
            "\"preview-etag\"",
            now.AddMinutes(3),
            CancellationToken.None));
        Assert.Single(store.EnqueuedItems);
    }

    private static Proposal CreateProposal(string id, string ownerUserId, ProposalStatus status) =>
        new()
        {
            Id = id,
            ConferenceId = "conference-1",
            OwnerUserId = ownerUserId,
            ProposalTypeId = "talk",
            FormVersion = 1,
            Answers = new Dictionary<string, string>(),
            Status = status
        };

    private sealed class FakeMembershipReader : IConferenceMembershipReader
    {
        public Task<ConferenceMembership?> GetMembershipAsync(
            string conferenceId,
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConferenceMembership?>(
                new ConferenceMembership(conferenceId, userId, ConferenceRole.Organizer, IsActive: true));
    }

    private sealed class FakeCampaignStore(Versioned<EmailCampaign> current) : IEmailCampaignStore
    {
        private Versioned<EmailCampaign> _current = current;

        public List<EmailOutboxItem> EnqueuedItems { get; } = [];

        public Task<Versioned<EmailCampaign>?> GetAsync(
            string conferenceId,
            string campaignId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Versioned<EmailCampaign>?>(_current);

        public Task<Versioned<EmailCampaign>> CreatePreviewAsync(
            EmailCampaign campaign,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<EmailCampaign>> ConfirmAndEnqueueAsync(
            EmailCampaign campaign,
            string expectedEtag,
            IReadOnlyList<EmailOutboxItem> outboxItems,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(_current.ETag, expectedEtag, StringComparison.Ordinal))
            {
                throw new RequestConflictException("ETag mismatch.");
            }

            EnqueuedItems.AddRange(outboxItems);
            _current = new Versioned<EmailCampaign>(campaign, "\"sent-etag\"");
            return Task.FromResult(_current);
        }
    }

    private sealed class FakeCampaignStoreForPreview : IEmailCampaignStore
    {
        public EmailCampaign? CreatedCampaign { get; private set; }

        public Task<Versioned<EmailCampaign>?> GetAsync(
            string conferenceId,
            string campaignId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Versioned<EmailCampaign>?>(null);

        public Task<Versioned<EmailCampaign>> CreatePreviewAsync(
            EmailCampaign campaign,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            CreatedCampaign = campaign;
            return Task.FromResult(new Versioned<EmailCampaign>(campaign, "\"preview-etag\""));
        }

        public Task<Versioned<EmailCampaign>> ConfirmAndEnqueueAsync(
            EmailCampaign campaign,
            string expectedEtag,
            IReadOnlyList<EmailOutboxItem> outboxItems,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProfileStore(IReadOnlyDictionary<string, SpeakerProfile> profiles) : IUserProfileStore
    {
        public Task<Versioned<SpeakerProfile>?> GetAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(profiles.TryGetValue(userId, out var profile)
                ? new Versioned<SpeakerProfile>(profile, "\"profile-etag\"")
                : null);

        public Task<Versioned<SpeakerProfile>> UpdateAsync(
            SpeakerProfile profile,
            string expectedEtag,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetEmailSuppressedAsync(
            string userId,
            bool suppressed,
            string reason,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProposalWorkflowStore(IReadOnlyList<Proposal> proposals) : IProposalWorkflowStore
    {
        public Task<Versioned<Proposal>> CreateDraftAsync(
            Proposal proposal,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            string idempotencyKey,
            string requestHash,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>?> GetOwnedAsync(
            string conferenceId,
            string proposalId,
            string ownerUserId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>?> GetByIdAsync(
            string conferenceId,
            string proposalId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>> UpdateAsync(
            Proposal proposal,
            string expectedEtag,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>> SubmitAsync(
            Proposal proposal,
            string expectedEtag,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>> WithdrawAsync(
            Proposal proposal,
            string expectedEtag,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>> SetPublicationConsentAsync(
            Proposal proposal,
            string expectedEtag,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Versioned<Proposal>> SetPublicationStateAsync(
            Proposal proposal,
            string expectedEtag,
            Cfp.Application.Auditing.AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProposalPage> ListByOwnerAsync(
            string ownerUserId,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProposalPage> ListByConferenceAsync(
            string conferenceId,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProposalPage(
                proposals.Select(proposal => new Versioned<Proposal>(proposal, "\"proposal-etag\"")).ToArray(),
                null));
    }
}
