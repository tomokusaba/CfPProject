using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.PublicConferences;
using Cfp.Domain.Conferences;
using Cfp.Domain.Proposals;
using Cfp.Domain.Scheduling;

namespace Cfp.Application.Scheduling;

public sealed record ScheduleSlotInput(
    string Id,
    string ProposalId,
    string RoomId,
    string? TrackId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

public sealed record SaveScheduleDraftCommand(
    IReadOnlyList<ScheduleRoom> Rooms,
    IReadOnlyList<ScheduleTrack> Tracks,
    IReadOnlyList<ScheduleSlotInput> Slots);

public sealed record PublicScheduleSession(
    string SessionId,
    string Title,
    string Abstract,
    string Speakers,
    string RoomName,
    string? TrackName,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int DurationMinutes);

public sealed record PublicSchedule(
    string ConferenceTitle,
    string TimeZoneId,
    IReadOnlyList<PublicScheduleSession> Sessions);

public sealed class SaveScheduleDraftHandler(
    IConferenceLifecycleStore conferences,
    IConferenceMembershipReader memberships,
    IReviewWorkflowStore proposals,
    IProposalTypeStore proposalTypes,
    IScheduleStore schedules)
{
    public async Task<Versioned<SchedulePlan>> HandleAsync(
        string conferenceId,
        Actor actor,
        SaveScheduleDraftCommand command,
        string? expectedEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await RequireOrganizerAsync(conferenceId, actor, memberships, cancellationToken);
        var conference = await conferences.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        var current = await schedules.GetDraftAsync(conferenceId, cancellationToken);
        if (current is null && expectedEtag is not null)
        {
            throw new RequestConflictException("The schedule draft does not exist.");
        }

        if (current is not null &&
            (string.IsNullOrWhiteSpace(expectedEtag) ||
             !string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal)))
        {
            throw new RequestConflictException("The schedule draft changed. Reload it before saving.");
        }

        if (command.Slots.Count > 500)
        {
            throw new ArgumentException("A schedule can contain at most 500 sessions.");
        }

        var slots = new List<ScheduleSlot>(command.Slots.Count);
        foreach (var slot in command.Slots)
        {
            var proposal = await proposals.GetProposalAsync(
                conferenceId,
                slot.ProposalId,
                cancellationToken) ?? throw new KeyNotFoundException($"Proposal {slot.ProposalId} was not found.");
            if (proposal.Value.Status != ProposalStatus.Accepted)
            {
                throw new InvalidOperationException($"Proposal {slot.ProposalId} has not been accepted.");
            }

            var proposalType = await proposalTypes.GetVersionAsync(
                conferenceId,
                proposal.Value.ProposalTypeId,
                proposal.Value.FormVersion,
                cancellationToken) ?? throw new InvalidOperationException("The proposal form version is unavailable.");

            slots.Add(new ScheduleSlot(
                slot.Id,
                slot.ProposalId,
                slot.RoomId,
                slot.TrackId,
                slot.StartsAtUtc.ToUniversalTime(),
                slot.EndsAtUtc.ToUniversalTime(),
                proposalType.DurationMinutes));
        }

        var plan = new SchedulePlan(
            conferenceId,
            (current?.Value.Revision ?? 0) + 1,
            command.Rooms,
            command.Tracks,
            slots,
            nowUtc.ToUniversalTime());
        var conflicts = ScheduleConflictPolicy.Validate(plan);
        if (conflicts.Count > 0)
        {
            throw new ScheduleValidationException(conflicts);
        }

        var audit = CreateAudit(
            conferenceId,
            actor.UserId,
            "ScheduleDraftSaved",
            $"Schedule revision {plan.Revision} saved.");
        return await schedules.SaveDraftAsync(plan, expectedEtag, audit, cancellationToken);
    }

    private static async Task RequireOrganizerAsync(
        string conferenceId,
        Actor actor,
        IConferenceMembershipReader memberships,
        CancellationToken cancellationToken)
    {
        var membership = await memberships.GetMembershipAsync(conferenceId, actor.UserId, cancellationToken);
        if (membership is null ||
            !membership.IsActive ||
            membership.Role is not (ConferenceRole.ConferenceOwner or ConferenceRole.Organizer))
        {
            throw new ConferenceAuthorizationException();
        }
    }

    internal static AuditEvent CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string summary) =>
        new($"audit:{Guid.NewGuid():N}", conferenceId, actorUserId, operation, "schedule", DateTimeOffset.UtcNow, summary);
}

public sealed class PublishScheduleHandler(
    IConferenceLifecycleStore conferences,
    IConferenceMembershipReader memberships,
    IReviewWorkflowStore proposals,
    IScheduleStore schedules)
{
    public async Task<Versioned<SchedulePlan>> HandleAsync(
        string conferenceId,
        Actor actor,
        string expectedDraftEtag,
        string? expectedPublicationEtag,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var membership = await memberships.GetMembershipAsync(conferenceId, actor.UserId, cancellationToken);
        if (membership is null ||
            !membership.IsActive ||
            membership.Role is not (ConferenceRole.ConferenceOwner or ConferenceRole.Organizer))
        {
            throw new ConferenceAuthorizationException();
        }

        var conference = await conferences.GetAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Conference was not found.");
        if (conference.Value.LifecycleState != ConferenceLifecycleState.Active ||
            conference.Value.Visibility != ConferenceVisibility.Public)
        {
            throw new InvalidOperationException("Publish the conference before publishing its schedule.");
        }

        var schedule = await schedules.GetDraftAsync(conferenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Schedule draft was not found.");
        if (!string.Equals(schedule.ETag, expectedDraftEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The schedule draft changed. Reload it before publishing.");
        }

        if (schedule.Value.Slots.Count == 0)
        {
            throw new InvalidOperationException("Add at least one session before publishing the schedule.");
        }

        foreach (var slot in schedule.Value.Slots)
        {
            var proposal = await proposals.GetProposalAsync(
                conferenceId,
                slot.ProposalId,
                cancellationToken) ?? throw new KeyNotFoundException($"Proposal {slot.ProposalId} was not found.");
            if (proposal.Value.Status != ProposalStatus.Accepted ||
                !proposal.Value.PublicationConsentConfirmed ||
                proposal.Value.PublicationState != ProposalPublicationState.Published)
            {
                throw new InvalidOperationException(
                    $"Session {slot.ProposalId} is not approved for public schedule publication.");
            }
        }

        var audit = SaveScheduleDraftHandler.CreateAudit(
            conferenceId,
            actor.UserId,
            "SchedulePublished",
            $"Schedule revision {schedule.Value.Revision} published.");
        return await schedules.PublishAsync(
            schedule.Value,
            expectedDraftEtag,
            expectedPublicationEtag,
            audit,
            cancellationToken);
    }
}

public sealed class GetPublicScheduleHandler(
    IPublicConferenceReader conferences,
    IScheduleStore schedules,
    IReviewWorkflowStore proposals)
{
    public async Task<PublicSchedule?> HandleAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var conferencePage = await conferences.GetBySlugAsync(slug, cancellationToken);
        if (conferencePage is null ||
            conferencePage.LifecycleState != ConferenceLifecycleState.Active ||
            conferencePage.Visibility != ConferenceVisibility.Public)
        {
            return null;
        }

        var published = await schedules.GetPublishedAsync(conferencePage.Id, cancellationToken);
        if (published is null)
        {
            return new PublicSchedule(conferencePage.Title, conferencePage.TimeZoneId, []);
        }

        var rooms = published.Value.Rooms.ToDictionary(room => room.Id, StringComparer.Ordinal);
        var tracks = published.Value.Tracks.ToDictionary(track => track.Id, StringComparer.Ordinal);
        var sessions = new List<PublicScheduleSession>();
        foreach (var slot in published.Value.Slots.OrderBy(slot => slot.StartsAtUtc))
        {
            var proposal = await proposals.GetProposalAsync(
                conferencePage.Id,
                slot.ProposalId,
                cancellationToken);
            if (proposal is null ||
                proposal.Value.Status != ProposalStatus.Accepted ||
                !proposal.Value.PublicationConsentConfirmed ||
                proposal.Value.PublicationState != ProposalPublicationState.Published)
            {
                continue;
            }

            proposal.Value.Answers.TryGetValue("title", out var title);
            proposal.Value.Answers.TryGetValue("abstract", out var abstractText);
            proposal.Value.Answers.TryGetValue("speakers", out var speakers);
            if (!rooms.TryGetValue(slot.RoomId, out var room))
            {
                continue;
            }

            var trackName = slot.TrackId is not null && tracks.TryGetValue(slot.TrackId, out var track)
                ? track.Name
                : null;
            sessions.Add(new PublicScheduleSession(
                slot.ProposalId,
                title ?? string.Empty,
                abstractText ?? string.Empty,
                speakers ?? string.Empty,
                room.Name,
                trackName,
                slot.StartsAtUtc,
                slot.EndsAtUtc,
                slot.ProposalDurationMinutes));
        }

        return new PublicSchedule(conferencePage.Title, conferencePage.TimeZoneId, sessions);
    }
}

public sealed class ScheduleValidationException(IReadOnlyList<string> errors)
    : Exception("The schedule contains invalid or conflicting slots.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
