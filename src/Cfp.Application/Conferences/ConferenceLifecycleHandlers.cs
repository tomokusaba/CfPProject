using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Conferences;

public sealed class PublishConferenceHandler(
    IConferenceLifecycleStore store,
    ConferenceAuthorizationService authorization)
{
    public async Task<Versioned<Conference>> HandleAsync(
        string conferenceId,
        Actor actor,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        var current = await store.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference changed. Reload it before publishing.");
        }

        var published = current.Value.Publish(nowUtc);
        var auditEvent = CreateAudit(
            conferenceId,
            actor.UserId,
            "ConferencePublished",
            "Conference visibility set to public.",
            nowUtc);
        return await store.SaveAsync(published, expectedEtag, auditEvent, cancellationToken);
    }

    private static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string summary,
        DateTimeOffset nowUtc) =>
        new(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actorUserId,
            operation,
            conferenceId,
            nowUtc.ToUniversalTime(),
            summary);
}

public sealed class UpdateConferenceHandler(
    IConferenceLifecycleStore store,
    ConferenceAuthorizationService authorization)
{
    public async Task<Versioned<Conference>> HandleAsync(
        string conferenceId,
        Actor actor,
        string title,
        string description,
        string timeZoneId,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        DateTimeOffset cfpOpensAtUtc,
        DateTimeOffset cfpClosesAtUtc,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        var current = await store.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference changed. Reload it before saving.");
        }

        var updated = current.Value.UpdateDetails(
            title,
            description,
            timeZoneId,
            startsAtUtc,
            endsAtUtc,
            cfpOpensAtUtc,
            cfpClosesAtUtc,
            nowUtc);
        var auditEvent = CreateAudit(
            conferenceId,
            actor.UserId,
            "ConferenceUpdated",
            "Conference details updated.",
            nowUtc);
        return await store.SaveAsync(updated, expectedEtag, auditEvent, cancellationToken);
    }

    private static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string summary,
        DateTimeOffset nowUtc) =>
        new(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actorUserId,
            operation,
            conferenceId,
            nowUtc.ToUniversalTime(),
            summary);
}

public sealed class ArchiveConferenceHandler(
    IConferenceLifecycleStore store,
    ConferenceAuthorizationService authorization)
{
    public async Task<Versioned<Conference>> HandleAsync(
        string conferenceId,
        Actor actor,
        string reason,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("An archive reason of at most 500 characters is required.", nameof(reason));
        }

        var current = await store.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference changed. Reload it before archiving.");
        }

        var archived = current.Value.Archive(nowUtc);
        var auditEvent = CreateAudit(
            conferenceId,
            actor.UserId,
            "ConferenceArchived",
            $"Conference archived. Reason: {reason.Trim()}",
            nowUtc);
        return await store.SaveAsync(archived, expectedEtag, auditEvent, cancellationToken);
    }

    private static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string summary,
        DateTimeOffset nowUtc) =>
        new(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actorUserId,
            operation,
            conferenceId,
            nowUtc.ToUniversalTime(),
            summary);
}

public sealed class SetCfpPublicationStateHandler(
    IConferenceLifecycleStore store,
    ConferenceAuthorizationService authorization)
{
    public async Task<Versioned<Conference>> HandleAsync(
        string conferenceId,
        Actor actor,
        CfpPublicationState state,
        string reason,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A reason of at most 500 characters is required.", nameof(reason));
        }

        var current = await store.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference changed. Reload it before changing the CFP.");
        }

        var updated = current.Value.SetCfpPublicationState(state, nowUtc);
        var auditEvent = CreateAudit(
            conferenceId,
            actor.UserId,
            "CfpStateChanged",
            $"CFP state changed to {state}. Reason: {reason.Trim()}",
            nowUtc);
        return await store.SaveAsync(updated, expectedEtag, auditEvent, cancellationToken);
    }

    private static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string summary,
        DateTimeOffset nowUtc) =>
        new(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actorUserId,
            operation,
            conferenceId,
            nowUtc.ToUniversalTime(),
            summary);
}

public sealed class SetPublicShowcaseHandler(
    IConferenceLifecycleStore store,
    ConferenceAuthorizationService authorization)
{
    public async Task<Versioned<Conference>> HandleAsync(
        string conferenceId,
        Actor actor,
        bool enabled,
        string reason,
        string expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await authorization.RequireRoleAsync(
            conferenceId,
            actor,
            cancellationToken,
            ConferenceRole.ConferenceOwner,
            ConferenceRole.Organizer);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A reason of at most 500 characters is required.", nameof(reason));
        }

        var current = await store.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference changed. Reload before updating the showcase setting.");
        }

        var updated = current.Value.SetPublicShowcaseEnabled(enabled, nowUtc);
        var auditEvent = CreateAudit(
            conferenceId,
            actor.UserId,
            enabled ? "PublicShowcaseEnabled" : "PublicShowcaseDisabled",
            $"Public proposal showcase {(enabled ? "enabled" : "disabled")}. Reason: {reason.Trim()}",
            nowUtc);
        return await store.SaveAsync(updated, expectedEtag, auditEvent, cancellationToken);
    }

    private static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string summary,
        DateTimeOffset nowUtc) =>
        new(
            $"audit:{Guid.NewGuid():N}",
            conferenceId,
            actorUserId,
            operation,
            conferenceId,
            nowUtc.ToUniversalTime(),
            summary);
}
