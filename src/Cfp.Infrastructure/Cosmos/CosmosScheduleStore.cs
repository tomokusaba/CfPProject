using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Domain.Scheduling;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosScheduleStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IScheduleStore
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<Versioned<SchedulePlan>?> GetDraftAsync(
        string conferenceId,
        CancellationToken cancellationToken)
    {
        var pointer = await ReadPointerAsync("schedule-draft", conferenceId, cancellationToken);
        if (pointer is null)
        {
            return null;
        }

        var revision = await ReadRevisionAsync(conferenceId, pointer.Value.Revision, cancellationToken);
        return new Versioned<SchedulePlan>(Map(revision), pointer.ETag);
    }

    public async Task<Versioned<SchedulePlan>?> GetPublishedAsync(
        string conferenceId,
        CancellationToken cancellationToken)
    {
        var pointer = await ReadPointerAsync("schedule-publication", conferenceId, cancellationToken);
        if (pointer is null || pointer.Value.State != "Published")
        {
            return null;
        }

        var revision = await ReadRevisionAsync(conferenceId, pointer.Value.Revision, cancellationToken);
        return new Versioned<SchedulePlan>(Map(revision), pointer.ETag);
    }

    public async Task<Versioned<SchedulePlan>> SaveDraftAsync(
        SchedulePlan plan,
        string? expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var current = await ReadPointerAsync("schedule-draft", plan.ConferenceId, cancellationToken);
        if ((current is null && expectedEtag is not null) ||
            (current is not null &&
             (expectedEtag is null || !string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))))
        {
            throw new RequestConflictException("The schedule draft changed. Reload it before saving.");
        }

        var revision = ToDocument(plan);
        var pointer = new SchedulePointerDocument
        {
            Id = "schedule-draft",
            ConferenceId = plan.ConferenceId,
            Type = "scheduleDraft",
            Revision = plan.Revision,
            State = "Draft",
            UpdatedAtUtc = plan.UpdatedAtUtc
        };
        var batch = _conferenceData.CreateTransactionalBatch(new PartitionKey(plan.ConferenceId))
            .CreateItem(revision);
        if (current is null)
        {
            batch.CreateItem(pointer);
        }
        else
        {
            batch.ReplaceItem(
                pointer.Id,
                pointer,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag });
        }

        batch.CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The schedule draft changed. Reload it before saving.");
            }

            throw ToCosmosException("Saving schedule draft", response);
        }

        return await GetDraftAsync(plan.ConferenceId, cancellationToken)
            ?? throw new InvalidOperationException("Saved schedule draft could not be read back.");
    }

    public async Task<Versioned<SchedulePlan>> PublishAsync(
        SchedulePlan plan,
        string expectedDraftEtag,
        string? expectedPublicationEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var draftPointer = await ReadPointerAsync("schedule-draft", plan.ConferenceId, cancellationToken)
            ?? throw new RequestConflictException("The schedule draft no longer exists.");
        if (!string.Equals(draftPointer.ETag, expectedDraftEtag, StringComparison.Ordinal) ||
            draftPointer.Value.Revision != plan.Revision)
        {
            throw new RequestConflictException("The schedule draft changed. Reload it before publishing.");
        }

        var publicationPointer = await ReadPointerAsync(
            "schedule-publication",
            plan.ConferenceId,
            cancellationToken);
        if ((publicationPointer is null && expectedPublicationEtag is not null) ||
            (publicationPointer is not null &&
             (expectedPublicationEtag is null ||
              !string.Equals(publicationPointer.ETag, expectedPublicationEtag, StringComparison.Ordinal))))
        {
            throw new RequestConflictException("The published schedule changed. Reload it before publishing.");
        }

        var updatedDraft = draftPointer.Value with { UpdatedAtUtc = auditEvent.OccurredAtUtc };
        var published = new SchedulePointerDocument
        {
            Id = "schedule-publication",
            ConferenceId = plan.ConferenceId,
            Type = "schedulePublication",
            Revision = plan.Revision,
            State = "Published",
            UpdatedAtUtc = auditEvent.OccurredAtUtc
        };
        var batch = _conferenceData.CreateTransactionalBatch(new PartitionKey(plan.ConferenceId))
            .ReplaceItem(
                updatedDraft.Id,
                updatedDraft,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedDraftEtag });
        if (publicationPointer is null)
        {
            batch.CreateItem(published);
        }
        else
        {
            batch.ReplaceItem(
                published.Id,
                published,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedPublicationEtag });
        }

        batch.CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The schedule changed. Reload it before publishing.");
            }

            throw ToCosmosException("Publishing schedule", response);
        }

        return await GetPublishedAsync(plan.ConferenceId, cancellationToken)
            ?? throw new InvalidOperationException("Published schedule could not be read back.");
    }

    private async Task<Versioned<SchedulePointerDocument>?> ReadPointerAsync(
        string id,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<SchedulePointerDocument>(
                id,
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<SchedulePointerDocument>(response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<ScheduleRevisionDocument> ReadRevisionAsync(
        string conferenceId,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<ScheduleRevisionDocument>(
                $"schedule-revision:{revision}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new JsonSerializationException("Schedule pointer references a missing revision.");
        }
    }

    private static ScheduleRevisionDocument ToDocument(SchedulePlan plan) => new()
    {
        Id = $"schedule-revision:{plan.Revision}",
        ConferenceId = plan.ConferenceId,
        Type = "scheduleRevision",
        Revision = plan.Revision,
        Rooms = plan.Rooms.Select(room => new ScheduleRoomDocument { Id = room.Id, Name = room.Name }).ToList(),
        Tracks = plan.Tracks.Select(track => new ScheduleTrackDocument { Id = track.Id, Name = track.Name }).ToList(),
        Slots = plan.Slots.Select(slot => new ScheduleSlotDocument
        {
            Id = slot.Id,
            ProposalId = slot.ProposalId,
            RoomId = slot.RoomId,
            TrackId = slot.TrackId,
            StartsAtUtc = slot.StartsAtUtc,
            EndsAtUtc = slot.EndsAtUtc,
            ProposalDurationMinutes = slot.ProposalDurationMinutes
        }).ToList(),
        UpdatedAtUtc = plan.UpdatedAtUtc
    };

    private static SchedulePlan Map(ScheduleRevisionDocument document) =>
        new(
            document.ConferenceId,
            document.Revision,
            document.Rooms.Select(room => new ScheduleRoom(room.Id, room.Name)).ToArray(),
            document.Tracks.Select(track => new ScheduleTrack(track.Id, track.Name)).ToArray(),
            document.Slots.Select(slot => new ScheduleSlot(
                slot.Id,
                slot.ProposalId,
                slot.RoomId,
                slot.TrackId,
                slot.StartsAtUtc,
                slot.EndsAtUtc,
                slot.ProposalDurationMinutes)).ToArray(),
            document.UpdatedAtUtc);

    private static AuditDocument ToDocument(AuditEvent auditEvent) => new()
    {
        Id = auditEvent.Id,
        ConferenceId = auditEvent.ConferenceId,
        Type = "auditEvent",
        ActorUserId = auditEvent.ActorUserId,
        Operation = auditEvent.Operation,
        TargetId = auditEvent.TargetId,
        OccurredAtUtc = auditEvent.OccurredAtUtc,
        Summary = auditEvent.Summary
    };

    private static CosmosException ToCosmosException(string operation, TransactionalBatchResponse response) =>
        new(
            $"{operation} failed with Cosmos DB status {(int)response.StatusCode}.",
            response.StatusCode,
            0,
            response.ActivityId,
            response.RequestCharge);

    private sealed record SchedulePointerDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("revision")]
        public int Revision { get; set; }
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;
        [JsonProperty("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed class ScheduleRevisionDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("revision")]
        public int Revision { get; set; }
        [JsonProperty("rooms")]
        public List<ScheduleRoomDocument> Rooms { get; set; } = [];
        [JsonProperty("tracks")]
        public List<ScheduleTrackDocument> Tracks { get; set; } = [];
        [JsonProperty("slots")]
        public List<ScheduleSlotDocument> Slots { get; set; } = [];
        [JsonProperty("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed class ScheduleRoomDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ScheduleTrackDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ScheduleSlotDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("proposalId")]
        public string ProposalId { get; set; } = string.Empty;
        [JsonProperty("roomId")]
        public string RoomId { get; set; } = string.Empty;
        [JsonProperty("trackId")]
        public string? TrackId { get; set; }
        [JsonProperty("startsAtUtc")]
        public DateTimeOffset StartsAtUtc { get; set; }
        [JsonProperty("endsAtUtc")]
        public DateTimeOffset EndsAtUtc { get; set; }
        [JsonProperty("proposalDurationMinutes")]
        public int ProposalDurationMinutes { get; set; }
    }

    private sealed class AuditDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("actorUserId")]
        public string ActorUserId { get; set; } = string.Empty;
        [JsonProperty("operation")]
        public string Operation { get; set; } = string.Empty;
        [JsonProperty("targetId")]
        public string TargetId { get; set; } = string.Empty;
        [JsonProperty("occurredAtUtc")]
        public DateTimeOffset OccurredAtUtc { get; set; }
        [JsonProperty("summary")]
        public string Summary { get; set; } = string.Empty;
    }
}
