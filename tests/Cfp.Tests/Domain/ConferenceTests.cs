using Cfp.Domain.Conferences;

namespace Cfp.Tests.Domain;

public sealed class ConferenceTests
{
    private static readonly DateTimeOffset Opens = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Closes = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetCfpAvailability_DerivesScheduledOpenAndClosedWithoutMutatingState()
    {
        var conference = CreateConference() with
        {
            LifecycleState = ConferenceLifecycleState.Active,
            Visibility = ConferenceVisibility.Public,
            CfpState = CfpPublicationState.Published
        };

        Assert.Equal(CfpAvailability.Scheduled, conference.GetCfpAvailability(Opens.AddDays(-1)));
        Assert.Equal(CfpAvailability.Open, conference.GetCfpAvailability(Opens));
        Assert.Equal(CfpAvailability.Closed, conference.GetCfpAvailability(Closes));
        Assert.Equal(CfpPublicationState.Published, conference.CfpState);
    }

    [Theory]
    [InlineData("Bad-slug")]
    [InlineData("-bad")]
    [InlineData("bad--slug")]
    [InlineData("bad_slug")]
    public void Create_RejectsInvalidSlug(string slug)
    {
        Assert.Throws<ArgumentException>(() => Conference.Create(
            "conf-1", slug, "Conference", "", "Asia/Tokyo",
            Opens.AddDays(30), Opens.AddDays(31), Opens, Closes));
    }

    [Fact]
    public void Publish_DoesNotImplicitlyPublishTheCfp()
    {
        var conference = CreateConference().Publish(Opens);

        Assert.Equal(ConferenceLifecycleState.Active, conference.LifecycleState);
        Assert.Equal(ConferenceVisibility.Public, conference.Visibility);
        Assert.Equal(CfpPublicationState.Draft, conference.CfpState);
    }

    [Fact]
    public void Archive_HidesConferenceAndManuallyClosesCfp()
    {
        var published = CreateConference().Publish(Opens) with
        {
            CfpState = CfpPublicationState.Published
        };

        var archived = published.Archive(Closes);

        Assert.Equal(ConferenceLifecycleState.Archived, archived.LifecycleState);
        Assert.Equal(ConferenceVisibility.Private, archived.Visibility);
        Assert.Equal(CfpPublicationState.ManuallyClosed, archived.CfpState);
    }

    private static Conference CreateConference() => Conference.Create(
        "conf-1",
        "example-conf",
        "Example conference",
        string.Empty,
        "Asia/Tokyo",
        Opens.AddDays(30),
        Opens.AddDays(31),
        Opens,
        Closes);
}
