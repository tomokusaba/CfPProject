using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;

namespace Cfp.Tests.Domain;

public sealed class ProposalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Submit_RequiresOpenCfpAndValidAnswers()
    {
        var conference = CreateConference();
        var type = CreateProposalType();
        var proposal = Proposal.CreateDraft(
            "proposal-1",
            conference.Id,
            "speaker-1",
            type,
            new Dictionary<string, string> { ["title"] = "A useful session" },
            Now);

        var submitted = proposal.Submit("speaker-1", conference, type, Now);

        Assert.Equal(ProposalStatus.Submitted, submitted.Status);
        Assert.Equal(Now, submitted.SubmittedAtUtc);
        Assert.Throws<InvalidOperationException>(() => proposal.Submit(
            "speaker-1",
            conference with { CfpState = CfpPublicationState.ManuallyClosed },
            type,
            Now));
    }

    [Fact]
    public void Submit_RejectsMissingRequiredAnswers()
    {
        var proposal = Proposal.CreateDraft(
            "proposal-1",
            "conf-1",
            "speaker-1",
            CreateProposalType(),
            new Dictionary<string, string>(),
            Now);

        var exception = Assert.Throws<ProposalValidationException>(() =>
            proposal.Submit("speaker-1", CreateConference(), CreateProposalType(), Now));

        Assert.Contains(exception.Errors, error => error.Contains("Title", StringComparison.Ordinal));
    }

    [Fact]
    public void SetPublicationState_RequiresAcceptanceAndSpeakerConsent()
    {
        var proposal = CreateSubmittedProposal().Decide(true, "Strong proposal", "organizer-1", Now);

        Assert.Throws<InvalidOperationException>(() => proposal.SetPublicationState(true, Now));
        var consented = proposal.SetPublicationConsent("speaker-1", true, "v1", Now);
        Assert.Equal(
            ProposalPublicationState.Published,
            consented.SetPublicationState(true, Now).PublicationState);
        Assert.Equal("speaker-1", consented.PublicationConsentUserId);
        Assert.Equal("v1", consented.PublicationConsentStatementVersion);
        Assert.Equal(Now, consented.PublicationConsentAtUtc);
        Assert.Equal(
            ProposalPublicationState.Hidden,
            proposal.SetPublicationConsent("speaker-1", false, null, Now).PublicationState);
    }

    [Fact]
    public void ProposalType_RejectsDuplicateFieldIdsAndEmptyChoiceOptions()
    {
        var type = CreateProposalType() with
        {
            Fields =
            [
                new ProposalFormField("choice", "Choice", FormFieldKind.SingleChoice, true, Options: []),
                new ProposalFormField("choice", "Duplicate", FormFieldKind.ShortText, false)
            ]
        };

        var errors = type.ValidateDefinition();

        Assert.Contains(errors, error => error.Contains("unique", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("options", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateAnswers_RejectsAnotherUsersChanges()
    {
        var proposal = CreateSubmittedProposal();
        Assert.Throws<UnauthorizedAccessException>(() => proposal.UpdateAnswers(
            "different-user",
            proposal.Answers,
            CreateProposalType(),
            Now,
            Now.AddDays(5)));
    }

    private static Proposal CreateSubmittedProposal()
    {
        var conference = CreateConference();
        var type = CreateProposalType();
        return Proposal.CreateDraft(
                "proposal-1",
                conference.Id,
                "speaker-1",
                type,
                new Dictionary<string, string> { ["title"] = "A useful session" },
                Now)
            .Submit("speaker-1", conference, type, Now);
    }

    private static Conference CreateConference() => Conference.Create(
        "conf-1", "example-conf", "Example", "", "Asia/Tokyo",
        Now.AddDays(30), Now.AddDays(31), Now.AddDays(-1), Now.AddDays(20)) with
    {
        LifecycleState = ConferenceLifecycleState.Active,
        Visibility = ConferenceVisibility.Public,
        CfpState = CfpPublicationState.Published
    };

    private static ProposalType CreateProposalType() => new()
    {
        Id = "talk-30",
        ConferenceId = "conf-1",
        Name = "30 minute talk",
        DurationMinutes = 30,
        FormVersion = 1,
        IsAcceptingSubmissions = true,
        Fields = [new ProposalFormField("title", "Title", FormFieldKind.ShortText, true, 100)]
    };
}
