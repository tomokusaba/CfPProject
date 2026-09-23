using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;

namespace Cfp.Application.Proposals;

public sealed record SaveProposalDraftCommand(
    string ConferenceId,
    string ProposalTypeId,
    IReadOnlyDictionary<string, string> Answers);

public sealed class SaveProposalDraftHandler(
    IConferenceLifecycleStore conferences,
    IProposalTypeStore proposalTypes,
    IProposalWorkflowStore proposals)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        SaveProposalDraftCommand command,
        Actor actor,
        string idempotencyKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(idempotencyKey, out var operationGuid))
        {
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(idempotencyKey));
        }

        var conference = await conferences.GetAsync(command.ConferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (!conference.Value.CanAcceptProposals(nowUtc))
        {
            throw new InvalidOperationException("This CFP is not accepting submissions.");
        }

        var proposalType = await proposalTypes.GetAsync(
            command.ConferenceId,
            command.ProposalTypeId,
            cancellationToken);
        if (proposalType is null ||
            !proposalType.Value.IsPublic ||
            !proposalType.Value.IsAcceptingSubmissions)
        {
            throw new KeyNotFoundException("Proposal type is not available for submission.");
        }

        var requestHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))))
            .ToLowerInvariant();
        var proposalId = "proposal_" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{actor.UserId}\n{operationGuid:N}")))
            .ToLowerInvariant()[..32];
        var proposal = Proposal.CreateDraft(
            proposalId,
            command.ConferenceId,
            actor.UserId,
            proposalType.Value,
            command.Answers,
            nowUtc);
        var auditEvent = ProposalAudit.Create(
            command.ConferenceId,
            actor.UserId,
            "ProposalDraftCreated",
            proposalId,
            nowUtc);

        return await proposals.CreateDraftAsync(
            proposal,
            auditEvent,
            operationGuid.ToString("N"),
            requestHash,
            cancellationToken);
    }
}

public sealed class UpdateProposalHandler(
    IConferenceLifecycleStore conferences,
    IProposalTypeStore proposalTypes,
    IProposalWorkflowStore proposals)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        IReadOnlyDictionary<string, string> answers,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetOwnedAsync(
            conferenceId,
            proposalId,
            actor.UserId,
            cancellationToken) ?? throw new KeyNotFoundException("Proposal was not found.");
        var conference = await conferences.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        var proposalType = await proposalTypes.GetVersionAsync(
            conferenceId,
            proposal.Value.ProposalTypeId,
            proposal.Value.FormVersion,
            cancellationToken) ?? throw new InvalidOperationException("The saved form version is unavailable.");

        var updated = proposal.Value.UpdateAnswers(
            actor.UserId,
            answers,
            proposalType,
            nowUtc,
            conference.Value.CfpClosesAtUtc);
        return await proposals.UpdateAsync(
            updated,
            expectedEtag,
            ProposalAudit.Create(conferenceId, actor.UserId, "ProposalUpdated", proposalId, nowUtc),
            cancellationToken);
    }
}

public sealed class SubmitProposalHandler(
    IConferenceLifecycleStore conferences,
    IProposalTypeStore proposalTypes,
    IProposalWorkflowStore proposals)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetOwnedAsync(
            conferenceId,
            proposalId,
            actor.UserId,
            cancellationToken) ?? throw new KeyNotFoundException("Proposal was not found.");
        var conference = await conferences.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        var proposalType = await proposalTypes.GetVersionAsync(
            conferenceId,
            proposal.Value.ProposalTypeId,
            proposal.Value.FormVersion,
            cancellationToken) ?? throw new InvalidOperationException("The saved form version is unavailable.");

        var submitted = proposal.Value.Submit(actor.UserId, conference.Value, proposalType, nowUtc);
        return await proposals.SubmitAsync(
            submitted,
            expectedEtag,
            ProposalAudit.Create(conferenceId, actor.UserId, "ProposalSubmitted", proposalId, nowUtc),
            cancellationToken);
    }
}

public sealed class WithdrawProposalHandler(
    IConferenceLifecycleStore conferences,
    IProposalWorkflowStore proposals)
{
    public async Task<Versioned<Proposal>> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetOwnedAsync(
            conferenceId,
            proposalId,
            actor.UserId,
            cancellationToken) ?? throw new KeyNotFoundException("Proposal was not found.");
        var conference = await conferences.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        var withdrawn = proposal.Value.Withdraw(
            actor.UserId,
            nowUtc,
            conference.Value.CfpClosesAtUtc);
        return await proposals.WithdrawAsync(
            withdrawn,
            expectedEtag,
            ProposalAudit.Create(conferenceId, actor.UserId, "ProposalWithdrawn", proposalId, nowUtc),
            cancellationToken);
    }
}

public sealed class ListMyProposalsHandler(IProposalWorkflowStore proposals)
{
    public Task<ProposalPage> HandleAsync(
        Actor actor,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken) =>
        proposals.ListByOwnerAsync(
            actor.UserId,
            Math.Clamp(pageSize, 1, 50),
            continuationToken,
            cancellationToken);
}

public sealed class GetMyProposalHandler(IProposalWorkflowStore proposals)
{
    public Task<Versioned<Proposal>?> HandleAsync(
        string conferenceId,
        string proposalId,
        Actor actor,
        CancellationToken cancellationToken) =>
        proposals.GetOwnedAsync(conferenceId, proposalId, actor.UserId, cancellationToken);
}

internal static class ProposalAudit
{
    public static AuditEvent Create(
        string conferenceId,
        string actorUserId,
        string operation,
        string proposalId,
        DateTimeOffset nowUtc) =>
        new(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actorUserId,
            operation,
            proposalId,
            nowUtc.ToUniversalTime(),
            operation switch
            {
                "ProposalSubmitted" => "Proposal submitted.",
                "ProposalWithdrawn" => "Proposal withdrawn.",
                _ => "Proposal updated."
            });
}
