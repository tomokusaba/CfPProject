using Cfp.Domain.Conferences;

namespace Cfp.Domain.Proposals;

public enum ProposalStatus
{
    Draft,
    Submitted,
    UnderReview,
    Accepted,
    Rejected,
    Withdrawn
}

public enum ProposalPublicationState
{
    Hidden,
    Published
}

public enum FormFieldKind
{
    ShortText,
    LongText,
    SingleChoice,
    MultipleChoice
}

public sealed record ProposalFormField(
    string Id,
    string Label,
    FormFieldKind Kind,
    bool IsRequired,
    int? MaximumLength = null,
    IReadOnlyList<string>? Options = null);

public sealed record ProposalType
{
    public required string Id { get; init; }
    public required string ConferenceId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required int DurationMinutes { get; init; }
    public required int FormVersion { get; init; }
    public bool IsAcceptingSubmissions { get; init; }
    public bool IsPublic { get; init; } = true;
    public required IReadOnlyList<ProposalFormField> Fields { get; init; }

    public IReadOnlyList<string> ValidateDefinition()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100)
        {
            errors.Add("Proposal type ID is required and must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(ConferenceId))
        {
            errors.Add("Conference ID is required.");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120)
        {
            errors.Add("Proposal type name is required and must be at most 120 characters.");
        }

        if (Description.Length > 10_000)
        {
            errors.Add("Proposal type description must be at most 10,000 characters.");
        }

        if (DurationMinutes is < 1 or > 480)
        {
            errors.Add("Duration must be between 1 and 480 minutes.");
        }

        if (FormVersion < 1)
        {
            errors.Add("Form version must be positive.");
        }

        if (Fields.Count is < 1 or > 50)
        {
            errors.Add("A form must contain between 1 and 50 fields.");
        }

        var fieldIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Id) || field.Id.Length > 80 || !fieldIds.Add(field.Id))
            {
                errors.Add("Form field IDs must be unique, non-empty, and at most 80 characters.");
            }

            if (string.IsNullOrWhiteSpace(field.Label) || field.Label.Length > 200)
            {
                errors.Add($"Field '{field.Id}' requires a label of at most 200 characters.");
            }

            if (field.MaximumLength is < 1 or > 20_000)
            {
                errors.Add($"Field '{field.Id}' has an invalid maximum length.");
            }

            var hasOptions = field.Options is { Count: > 0 };
            if (field.Kind is FormFieldKind.SingleChoice or FormFieldKind.MultipleChoice)
            {
                if (!hasOptions || field.Options!.Count > 100 ||
                    field.Options.Any(string.IsNullOrWhiteSpace) ||
                    field.Options.Distinct(StringComparer.Ordinal).Count() != field.Options.Count)
                {
                    errors.Add($"Choice field '{field.Id}' requires 1 to 100 unique, non-empty options.");
                }
            }
            else if (hasOptions)
            {
                errors.Add($"Text field '{field.Id}' cannot define choices.");
            }
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateAnswers(IReadOnlyDictionary<string, string> answers)
    {
        var errors = new List<string>();
        var fieldIds = Fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var answerKey in answers.Keys)
        {
            if (!fieldIds.Contains(answerKey))
            {
                errors.Add($"Unknown form field: {answerKey}.");
            }
        }

        foreach (var field in Fields)
        {
            answers.TryGetValue(field.Id, out var value);
            value ??= string.Empty;

            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field.Label} is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (field.MaximumLength is > 0 && value.Length > field.MaximumLength)
            {
                errors.Add($"{field.Label} must be at most {field.MaximumLength} characters.");
            }

            if (field.Kind is FormFieldKind.SingleChoice or FormFieldKind.MultipleChoice &&
                field.Options is { Count: > 0 })
            {
                var selectedOptions = field.Kind == FormFieldKind.SingleChoice
                    ? [value]
                    : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (selectedOptions.Any(option => !field.Options.Contains(option, StringComparer.Ordinal)))
                {
                    errors.Add($"{field.Label} contains an invalid option.");
                }
            }
        }

        return errors;
    }
}

public sealed record Proposal
{
    public required string Id { get; init; }
    public required string ConferenceId { get; init; }
    public required string OwnerUserId { get; init; }
    public required string ProposalTypeId { get; init; }
    public required int FormVersion { get; init; }
    public required IReadOnlyDictionary<string, string> Answers { get; init; }
    public required ProposalStatus Status { get; init; }
    public ProposalPublicationState PublicationState { get; init; } = ProposalPublicationState.Hidden;
    public bool PublicationConsentConfirmed { get; init; }
    public string? PublicationConsentUserId { get; init; }
    public DateTimeOffset? PublicationConsentAtUtc { get; init; }
    public string? PublicationConsentStatementVersion { get; init; }
    public DateTimeOffset? SubmittedAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string? DecisionReason { get; init; }

    public static Proposal CreateDraft(
        string id,
        string conferenceId,
        string ownerUserId,
        ProposalType proposalType,
        IReadOnlyDictionary<string, string> answers,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(conferenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        ArgumentNullException.ThrowIfNull(proposalType);
        ArgumentNullException.ThrowIfNull(answers);

        if (proposalType.ConferenceId != conferenceId)
        {
            throw new InvalidOperationException("Proposal type belongs to a different conference.");
        }

        return new Proposal
        {
            Id = id,
            ConferenceId = conferenceId,
            OwnerUserId = ownerUserId,
            ProposalTypeId = proposalType.Id,
            FormVersion = proposalType.FormVersion,
            Answers = new Dictionary<string, string>(answers, StringComparer.Ordinal),
            Status = ProposalStatus.Draft,
            CreatedAtUtc = nowUtc.ToUniversalTime(),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal UpdateAnswers(
        string actorUserId,
        IReadOnlyDictionary<string, string> answers,
        ProposalType proposalType,
        DateTimeOffset nowUtc,
        DateTimeOffset cfpClosesAtUtc)
    {
        EnsureOwner(actorUserId);
        if (Status is not (ProposalStatus.Draft or ProposalStatus.Submitted))
        {
            throw new InvalidOperationException("This proposal can no longer be edited.");
        }

        if (nowUtc.ToUniversalTime() >= cfpClosesAtUtc.ToUniversalTime())
        {
            throw new InvalidOperationException("The CFP deadline has passed.");
        }

        if (proposalType.Id != ProposalTypeId || proposalType.FormVersion != FormVersion)
        {
            throw new InvalidOperationException("The original proposal form version is no longer available.");
        }

        var errors = proposalType.ValidateAnswers(answers);
        if (Status != ProposalStatus.Draft && errors.Count > 0)
        {
            throw new ProposalValidationException(errors);
        }

        return this with
        {
            Answers = new Dictionary<string, string>(answers, StringComparer.Ordinal),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal Submit(
        string actorUserId,
        Conference conference,
        ProposalType proposalType,
        DateTimeOffset nowUtc)
    {
        EnsureOwner(actorUserId);
        if (Status != ProposalStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft proposal can be submitted.");
        }

        if (!conference.CanAcceptProposals(nowUtc) || !proposalType.IsAcceptingSubmissions)
        {
            throw new InvalidOperationException("This CFP is not accepting submissions.");
        }

        var errors = proposalType.ValidateAnswers(Answers);
        if (errors.Count > 0)
        {
            throw new ProposalValidationException(errors);
        }

        return this with
        {
            Status = ProposalStatus.Submitted,
            SubmittedAtUtc = nowUtc.ToUniversalTime(),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal Withdraw(string actorUserId, DateTimeOffset nowUtc, DateTimeOffset cfpClosesAtUtc)
    {
        EnsureOwner(actorUserId);
        if (Status is not (ProposalStatus.Draft or ProposalStatus.Submitted or ProposalStatus.UnderReview))
        {
            throw new InvalidOperationException("This proposal cannot be withdrawn in its current state.");
        }

        if (nowUtc.ToUniversalTime() >= cfpClosesAtUtc.ToUniversalTime())
        {
            throw new InvalidOperationException("The CFP deadline has passed.");
        }

        return this with
        {
            Status = ProposalStatus.Withdrawn,
            PublicationState = ProposalPublicationState.Hidden,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal Decide(bool accepted, string reason, string actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        if (Status is not (ProposalStatus.Submitted or ProposalStatus.UnderReview))
        {
            throw new InvalidOperationException("Only submitted proposals can receive a decision.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A decision reason is required.", nameof(reason));
        }

        return this with
        {
            Status = accepted ? ProposalStatus.Accepted : ProposalStatus.Rejected,
            PublicationState = ProposalPublicationState.Hidden,
            DecisionReason = reason.Trim(),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal MarkUnderReview(DateTimeOffset nowUtc)
    {
        if (Status is not (ProposalStatus.Submitted or ProposalStatus.UnderReview))
        {
            throw new InvalidOperationException("Only submitted proposals can enter review.");
        }

        return this with
        {
            Status = ProposalStatus.UnderReview,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal SetPublicationConsent(
        string actorUserId,
        bool confirmed,
        string? statementVersion,
        DateTimeOffset nowUtc)
    {
        EnsureOwner(actorUserId);

        if (confirmed && Status != ProposalStatus.Accepted)
        {
            throw new InvalidOperationException("Only accepted proposals can be made public.");
        }

        if (confirmed && string.IsNullOrWhiteSpace(statementVersion))
        {
            throw new ArgumentException("Consent statement version is required.", nameof(statementVersion));
        }

        return this with
        {
            PublicationConsentConfirmed = confirmed,
            PublicationState = confirmed ? PublicationState : ProposalPublicationState.Hidden,
            PublicationConsentUserId = confirmed ? actorUserId : null,
            PublicationConsentAtUtc = confirmed ? nowUtc.ToUniversalTime() : null,
            PublicationConsentStatementVersion = confirmed ? statementVersion : null,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public Proposal SetPublicationState(bool published, DateTimeOffset nowUtc)
    {
        if (published && (Status != ProposalStatus.Accepted || !PublicationConsentConfirmed))
        {
            throw new InvalidOperationException("Publication requires an accepted proposal and speaker consent.");
        }

        return this with
        {
            PublicationState = published ? ProposalPublicationState.Published : ProposalPublicationState.Hidden,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    private void EnsureOwner(string actorUserId)
    {
        if (!string.Equals(OwnerUserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the proposal owner can perform this action.");
        }
    }
}

public sealed class ProposalValidationException(IReadOnlyList<string> errors)
    : Exception("Proposal answers did not satisfy the current form.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
