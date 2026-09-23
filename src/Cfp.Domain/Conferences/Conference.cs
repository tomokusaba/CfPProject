namespace Cfp.Domain.Conferences;

public enum ConferenceLifecycleState
{
    Draft,
    Active,
    Archived
}

public enum ConferenceVisibility
{
    Private,
    Public
}

public enum CfpPublicationState
{
    Draft,
    Published,
    ManuallyClosed
}

public enum CfpAvailability
{
    Unpublished,
    Scheduled,
    Open,
    Closed
}

public sealed record Conference
{
    private Conference()
    {
    }

    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public required ConferenceLifecycleState LifecycleState { get; init; }
    public required ConferenceVisibility Visibility { get; init; }
    public required string TimeZoneId { get; init; }
    public required DateTimeOffset StartsAtUtc { get; init; }
    public required DateTimeOffset EndsAtUtc { get; init; }
    public required CfpPublicationState CfpState { get; init; }
    public required DateTimeOffset CfpOpensAtUtc { get; init; }
    public required DateTimeOffset CfpClosesAtUtc { get; init; }
    public bool PublicShowcaseEnabled { get; init; }
    public int OwnerRosterRevision { get; init; }

    public static Conference Create(
        string id,
        string slug,
        string title,
        string description,
        string timeZoneId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset cfpOpensAtUtc,
        DateTimeOffset cfpClosesAtUtc)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A conference ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 80 ||
            slug.Any(character => !(char.IsAsciiLetterLower(character) ||
                                    char.IsAsciiDigit(character) ||
                                    character == '-')) ||
            slug[0] == '-' || slug[^1] == '-' || slug.Contains("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Slug must use lowercase letters, digits, and single hyphens.", nameof(slug));
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new ArgumentException("Title is required and must be at most 200 characters.", nameof(title));
        }

        if (description.Length > 10_000)
        {
            throw new ArgumentException("Description must be at most 10,000 characters.", nameof(description));
        }

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("The conference end must be after its start.", nameof(endsAtUtc));
        }

        if (cfpClosesAtUtc <= cfpOpensAtUtc)
        {
            throw new ArgumentException("The CFP closing time must be after its opening time.", nameof(cfpClosesAtUtc));
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("A time zone is required.", nameof(timeZoneId));
        }

        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        return new Conference
        {
            Id = id,
            Slug = slug,
            Title = title.Trim(),
            Description = description.Trim(),
            LifecycleState = ConferenceLifecycleState.Draft,
            Visibility = ConferenceVisibility.Private,
            TimeZoneId = timeZoneId,
            StartsAtUtc = startsAtUtc.ToUniversalTime(),
            EndsAtUtc = endsAtUtc.ToUniversalTime(),
            CfpState = CfpPublicationState.Draft,
            CfpOpensAtUtc = cfpOpensAtUtc.ToUniversalTime(),
            CfpClosesAtUtc = cfpClosesAtUtc.ToUniversalTime()
        };
    }

    public CfpAvailability GetCfpAvailability(DateTimeOffset nowUtc)
    {
        if (CfpState == CfpPublicationState.Draft)
        {
            return CfpAvailability.Unpublished;
        }

        if (CfpState == CfpPublicationState.ManuallyClosed ||
            nowUtc.ToUniversalTime() >= CfpClosesAtUtc)
        {
            return CfpAvailability.Closed;
        }

        return nowUtc.ToUniversalTime() < CfpOpensAtUtc
            ? CfpAvailability.Scheduled
            : CfpAvailability.Open;
    }

    public bool CanAcceptProposals(DateTimeOffset nowUtc) =>
        LifecycleState == ConferenceLifecycleState.Active &&
        Visibility == ConferenceVisibility.Public &&
        GetCfpAvailability(nowUtc) == CfpAvailability.Open;

    public Conference SetVisibility(ConferenceVisibility visibility, DateTimeOffset nowUtc)
    {
        if (visibility == ConferenceVisibility.Public &&
            LifecycleState != ConferenceLifecycleState.Active)
        {
            throw new InvalidOperationException("Only an active conference can be made public.");
        }

        return this with
        {
            Visibility = visibility,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Conference UpdateDetails(
        string title,
        string description,
        string timeZoneId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset cfpOpensAtUtc,
        DateTimeOffset cfpClosesAtUtc,
        DateTimeOffset nowUtc)
    {
        if (LifecycleState == ConferenceLifecycleState.Archived)
        {
            throw new InvalidOperationException("An archived conference cannot be edited.");
        }

        var updated = Create(
            Id,
            Slug,
            title,
            description,
            timeZoneId,
            startsAtUtc,
            endsAtUtc,
            cfpOpensAtUtc,
            cfpClosesAtUtc);
        return updated with
        {
            LifecycleState = LifecycleState,
            Visibility = Visibility,
            CfpState = CfpState,
            PublicShowcaseEnabled = PublicShowcaseEnabled,
            OwnerRosterRevision = OwnerRosterRevision,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Conference Archive(DateTimeOffset nowUtc) =>
        this with
        {
            LifecycleState = ConferenceLifecycleState.Archived,
            Visibility = ConferenceVisibility.Private,
            CfpState = CfpPublicationState.ManuallyClosed,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };

    public Conference Publish(DateTimeOffset nowUtc)
    {
        if (LifecycleState == ConferenceLifecycleState.Archived)
        {
            throw new InvalidOperationException("An archived conference cannot be published.");
        }

        return this with
        {
            LifecycleState = ConferenceLifecycleState.Active,
            Visibility = ConferenceVisibility.Public,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Conference SetCfpPublicationState(CfpPublicationState state, DateTimeOffset nowUtc)
    {
        if (LifecycleState == ConferenceLifecycleState.Archived &&
            state != CfpPublicationState.ManuallyClosed)
        {
            throw new InvalidOperationException("An archived conference cannot reopen its CFP.");
        }

        return this with
        {
            CfpState = state,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Conference SetPublicShowcaseEnabled(bool enabled, DateTimeOffset nowUtc) =>
        this with
        {
            PublicShowcaseEnabled = enabled,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
