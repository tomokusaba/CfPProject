using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Identity;
using Cfp.Application.PublicConferences;
using Cfp.Domain.Proposals;

namespace Cfp.Application.ProposalTypes;

public sealed class ProposalDefinitionValidationException(IReadOnlyList<string> errors)
    : Exception("Proposal type definition is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record SaveProposalTypeCommand(
    string? ProposalTypeId,
    string Name,
    string Description,
    int DurationMinutes,
    bool IsAcceptingSubmissions,
    bool IsPublic,
    IReadOnlyList<ProposalFormField> Fields);

public sealed class SaveProposalTypeHandler(IProposalTypeStore store)
{
    public async Task<Versioned<ProposalType>> HandleAsync(
        string conferenceId,
        SaveProposalTypeCommand command,
        Actor actor,
        string? expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        Versioned<ProposalType>? current = null;
        if (!string.IsNullOrWhiteSpace(command.ProposalTypeId))
        {
            current = await store.GetAsync(conferenceId, command.ProposalTypeId, cancellationToken);
            if (current is null)
            {
                throw new KeyNotFoundException("Proposal type was not found.");
            }

            if (string.IsNullOrWhiteSpace(expectedEtag))
            {
                throw new ArgumentException("If-Match is required when updating a proposal type.");
            }

            if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
            {
                throw new RequestConflictException("The proposal type changed. Reload it before saving.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(expectedEtag))
        {
            throw new ArgumentException("If-Match cannot be used when creating a proposal type.");
        }

        var proposalType = new ProposalType
        {
            Id = current?.Value.Id ?? Guid.NewGuid().ToString("N"),
            ConferenceId = conferenceId,
            Name = command.Name.Trim(),
            Description = command.Description.Trim(),
            DurationMinutes = command.DurationMinutes,
            FormVersion = (current?.Value.FormVersion ?? 0) + 1,
            IsAcceptingSubmissions = command.IsAcceptingSubmissions,
            IsPublic = command.IsPublic,
            Fields = command.Fields.ToArray()
        };
        var errors = proposalType.ValidateDefinition();
        if (errors.Count > 0)
        {
            throw new ProposalDefinitionValidationException(errors);
        }

        var auditEvent = new AuditEvent(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actor.UserId,
            current is null ? "ProposalTypeCreated" : "ProposalTypeVersionCreated",
            proposalType.Id,
            nowUtc.ToUniversalTime(),
            $"Proposal type form version {proposalType.FormVersion} saved.");

        return await store.SaveNewVersionAsync(
            proposalType,
            expectedEtag,
            auditEvent,
            cancellationToken);
    }
}

public sealed class GetPublicProposalTypesHandler(
    IPublicConferenceReader conferences,
    IProposalTypeStore proposalTypes)
{
    public async Task<IReadOnlyList<ProposalType>?> HandleAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var conference = await conferences.GetBySlugAsync(slug, cancellationToken);
        if (conference is null)
        {
            return null;
        }

        if (conference.CfpState == Cfp.Domain.Conferences.CfpPublicationState.Draft)
        {
            return [];
        }

        var types = await proposalTypes.ListAsync(conference.Id, cancellationToken);
        return types
            .Select(versioned => versioned.Value)
            .Where(type => type.IsPublic)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class ListManagedProposalTypesHandler(IProposalTypeStore proposalTypes)
{
    public Task<IReadOnlyList<Versioned<ProposalType>>> HandleAsync(
        string conferenceId,
        CancellationToken cancellationToken) =>
        proposalTypes.ListAsync(conferenceId, cancellationToken);
}
