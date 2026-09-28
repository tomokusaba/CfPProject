using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Authorization;
using Cfp.Application.Conferences;
using Cfp.Domain.Conferences;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosConferenceManagementStore(
    CosmosClient cosmosClient,
    IConfiguration configuration)
    : IConferenceManagementStore,
      IConferenceMembershipReader,
      IConferenceMembershipManagementStore,
      IConferenceLifecycleStore,
      IManagedConferenceReader
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");
    private readonly Container _conferenceDirectory =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceDirectory");
    private readonly Container _userProfilesForMembershipCheck =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "userProfiles");

    public async Task<Conference> CreateAsync(
        Conference conference,
        ConferenceMembership ownerMembership,
        AuditEvent auditEvent,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var directory = new ConferenceDirectoryEntry
        {
            Id = "entry",
            Slug = conference.Slug,
            ConferenceId = conference.Id,
            State = "preparing",
            Title = conference.Title,
            StartsAtUtc = conference.StartsAtUtc,
            TimeZoneId = conference.TimeZoneId,
            CfpState = conference.CfpState.ToString(),
            CfpOpensAtUtc = conference.CfpOpensAtUtc,
            CfpClosesAtUtc = conference.CfpClosesAtUtc,
            OperationId = idempotencyKey,
            RequestHash = requestHash
        };

        try
        {
            var createResponse = await _conferenceDirectory.CreateItemAsync(
                directory,
                new PartitionKey(conference.Slug),
                cancellationToken: cancellationToken);
            directory = createResponse.Resource;
        }

        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existingResponse = await _conferenceDirectory.ReadItemAsync<ConferenceDirectoryEntry>(
                "entry",
                new PartitionKey(conference.Slug),
                cancellationToken: cancellationToken);
            directory = existingResponse.Resource;
            if (directory.OperationId != idempotencyKey ||
                !string.Equals(directory.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new RequestConflictException("That conference slug is already in use.");
            }

            if (directory.ConferenceId != conference.Id)
            {
                throw new RequestConflictException("The idempotent conference operation does not match.");
            }
        }

        if (directory.State != "preparing")
        {
            var existingConference = await GetAsync(conference.Id, cancellationToken);
            return existingConference?.Value
                ?? throw new InvalidOperationException("Idempotent conference operation has no conference record.");
        }

        var marker = new OperationMarker
        {
            Id = $"operation:{idempotencyKey}",
            ConferenceId = conference.Id,
            Type = "operation",
            OperationId = idempotencyKey,
            RequestHash = requestHash
        };
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(conference.Id))
            .CreateItem(ToDocument(conference, auditEvent.OccurredAtUtc, auditEvent.OccurredAtUtc))
            .CreateItem(ToDocument(ownerMembership))
            .CreateItem(ToDocument(auditEvent))
            .CreateItem(marker);
        using var batchResponse = await batch.ExecuteAsync(cancellationToken);

        if (!batchResponse.IsSuccessStatusCode)
        {
            var existingMarker = await FindOperationMarkerAsync(
                conference.Id,
                idempotencyKey,
                cancellationToken);
            if (existingMarker is null ||
                !string.Equals(existingMarker.RequestHash, requestHash, StringComparison.Ordinal))
            {
                if (batchResponse.StatusCode == HttpStatusCode.Conflict)
                {
                    throw new RequestConflictException("The conference creation request conflicts with existing data.");
                }

                throw new CosmosException(
                    $"Conference creation failed with Cosmos DB status {(int)batchResponse.StatusCode}.",
                    batchResponse.StatusCode,
                    0,
                    batchResponse.ActivityId,
                    batchResponse.RequestCharge);
            }
        }

        var latestDirectoryResponse = await _conferenceDirectory.ReadItemAsync<ConferenceDirectoryEntry>(
            "entry",
            new PartitionKey(conference.Slug),
            cancellationToken: cancellationToken);
        var latestDirectory = latestDirectoryResponse.Resource;
        if (latestDirectory.State != "draft")
        {
            latestDirectory.State = "draft";
            try
            {
                await _conferenceDirectory.ReplaceItemAsync(
                    latestDirectory,
                    "entry",
                    new PartitionKey(conference.Slug),
                    new ItemRequestOptions { IfMatchEtag = latestDirectoryResponse.ETag },
                    cancellationToken);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                var latestResponse = await _conferenceDirectory.ReadItemAsync<ConferenceDirectoryEntry>(
                    "entry",
                    new PartitionKey(conference.Slug),
                    cancellationToken: cancellationToken);
                if (latestResponse.Resource.State != "draft")
                {
                    throw;
                }
            }
        }

        return conference;
    }

    public async Task<ConferenceMembership?> GetMembershipAsync(
        string conferenceId,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<MembershipDocument>(
                $"membership:{userId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            if (!Enum.TryParse<ConferenceRole>(response.Resource.Role, ignoreCase: false, out var role))
            {
                throw new JsonSerializationException("Invalid conference role in stored membership.");
            }

            return new ConferenceMembership(
                response.Resource.ConferenceId,
                response.Resource.UserId,
                role,
                response.Resource.State == "Active");
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ManagedConferencePage> ListAsync(
        string userId,
        int pageSize,
        string? continuationToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type AND c.userId = @userId AND c.state = @state AND (c.role = @ownerRole OR c.role = @organizerRole)")
            .WithParameter("@type", "conferenceMembership")
            .WithParameter("@userId", userId)
            .WithParameter("@state", "Active")
            .WithParameter("@ownerRole", ConferenceRole.ConferenceOwner.ToString())
            .WithParameter("@organizerRole", ConferenceRole.Organizer.ToString());
        using var iterator = _conferenceData.GetItemQueryIterator<MembershipDocument>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
        {
            return new ManagedConferencePage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        var conferences = new List<ManagedConferenceSummary>();
        foreach (var membership in response.Resource)
        {
            var conference = await GetAsync(membership.ConferenceId, cancellationToken);
            if (conference is null)
            {
                throw new JsonSerializationException("An active conference membership references a missing conference.");
            }

            if (!Enum.TryParse<ConferenceRole>(membership.Role, ignoreCase: false, out var role))
            {
                throw new JsonSerializationException("Invalid conference role in stored membership.");
            }

            conferences.Add(new ManagedConferenceSummary(conference.Value, conference.ETag, role));
        }

        return new ManagedConferencePage(conferences, response.ContinuationToken);
    }

    public async Task<IReadOnlyList<ConferenceMembership>> ListAsync(
        string conferenceId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type")
            .WithParameter("@type", "conferenceMembership");
        using var iterator = _conferenceData.GetItemQueryIterator<MembershipDocument>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = 100
            });

        var memberships = new List<ConferenceMembership>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in response.Resource)
            {
                if (!Enum.TryParse<ConferenceRole>(document.Role, ignoreCase: false, out var role))
                {
                    throw new JsonSerializationException("Invalid conference role in stored membership.");
                }

                memberships.Add(new ConferenceMembership(
                    document.ConferenceId,
                    document.UserId,
                    role,
                    document.State == "Active"));
            }
        }

        return memberships;
    }

    public async Task<Versioned<ConferenceMembership>> SetRoleAsync(
        string conferenceId,
        string userId,
        ConferenceRole role,
        string expectedRosterEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await _userProfilesForMembershipCheck.ReadItemAsync<UserProfileReference>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException("The user must sign in before they can be added to a conference.");
        }

        var conferenceResponse = await _conferenceData.ReadItemAsync<ConferenceDataDocument>(
            "conference",
            new PartitionKey(conferenceId),
            cancellationToken: cancellationToken);
        if (!string.Equals(conferenceResponse.ETag, expectedRosterEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference roster changed. Reload before updating membership.");
        }

        var existing = await ReadMembershipAsync(conferenceId, userId, cancellationToken);
        if (existing?.Resource.Role == ConferenceRole.ConferenceOwner.ToString() &&
            role != ConferenceRole.ConferenceOwner &&
            await GetActiveOwnerCountAsync(conferenceId, cancellationToken) <= 1)
        {
            throw new InvalidOperationException("A conference must retain at least one active owner.");
        }

        var updatedConference = conferenceResponse.Resource;
        updatedConference.OwnerRosterRevision++;
        updatedConference.UpdatedAtUtc = auditEvent.OccurredAtUtc;
        var membership = new MembershipDocument
        {
            Id = $"membership:{userId}",
            ConferenceId = conferenceId,
            Type = "conferenceMembership",
            UserId = userId,
            Role = role.ToString(),
            State = "Active"
        };
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(conferenceId))
            .ReplaceItem(
                "conference",
                updatedConference,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedRosterEtag });
        if (existing is null)
        {
            batch.CreateItem(membership);
        }
        else
        {
            batch.ReplaceItem(
                membership.Id,
                membership,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = existing.ETag });
        }

        batch.CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                throw new RequestConflictException("The conference roster changed. Reload before updating membership.");
            }

            throw new CosmosException(
                $"Updating conference membership failed with Cosmos DB status {(int)response.StatusCode}.",
                response.StatusCode,
                0,
                response.ActivityId,
                response.RequestCharge);
        }

        var updatedConferenceVersion = await GetAsync(conferenceId, cancellationToken)
            ?? throw new InvalidOperationException("Updated conference could not be read back.");
        return new Versioned<ConferenceMembership>(
            new ConferenceMembership(conferenceId, userId, role, true),
            updatedConferenceVersion.ETag);
    }

    private async Task<ItemResponse<MembershipDocument>?> ReadMembershipAsync(
        string conferenceId,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _conferenceData.ReadItemAsync<MembershipDocument>(
                $"membership:{userId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> GetActiveOwnerCountAsync(
        string conferenceId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE c.type = @type AND c.role = @role AND c.state = @state")
            .WithParameter("@type", "conferenceMembership")
            .WithParameter("@role", ConferenceRole.ConferenceOwner.ToString())
            .WithParameter("@state", "Active");
        using var iterator = _conferenceData.GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = 1
            });
        var response = await iterator.ReadNextAsync(cancellationToken);
        return response.Resource.FirstOrDefault();
    }

    public async Task<Versioned<Conference>?> GetAsync(
        string conferenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<ConferenceDataDocument>(
                "conference",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<Conference>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Versioned<Conference>> SaveAsync(
        Conference conference,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var currentResponse = await _conferenceData.ReadItemAsync<ConferenceDataDocument>(
            "conference",
            new PartitionKey(conference.Id),
            cancellationToken: cancellationToken);
        if (!string.Equals(currentResponse.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The conference changed. Reload it before saving.");
        }

        var wasPublic = IsPublic(currentResponse.Resource);
        var isPublic = conference.LifecycleState == ConferenceLifecycleState.Active &&
                       conference.Visibility == ConferenceVisibility.Public;
        if (wasPublic && !isPublic)
        {
            await UpdateDirectoryAsync("private", conference, cancellationToken);
        }

        var replacement = ToDocument(
            conference,
            currentResponse.Resource.CreatedAtUtc,
            auditEvent.OccurredAtUtc);
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(conference.Id))
            .ReplaceItem(
                "conference",
                replacement,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag })
            .CreateItem(ToDocument(auditEvent));
        using var batchResponse = await batch.ExecuteAsync(cancellationToken);
        if (!batchResponse.IsSuccessStatusCode)
        {
            if (batchResponse.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                throw new RequestConflictException("The conference changed. Reload it before saving.");
            }

            throw new CosmosException(
                $"Saving conference failed with Cosmos DB status {(int)batchResponse.StatusCode}.",
                batchResponse.StatusCode,
                0,
                batchResponse.ActivityId,
                batchResponse.RequestCharge);
        }

        if (isPublic)
        {
            await UpdateDirectoryAsync("active", conference, cancellationToken);
        }

        var saved = await GetAsync(conference.Id, cancellationToken);
        return saved ?? throw new InvalidOperationException("Saved conference could not be read back.");
    }

    private async Task<OperationMarker?> FindOperationMarkerAsync(
        string conferenceId,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<OperationMarker>(
                $"operation:{operationId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static ConferenceDataDocument ToDocument(
        Conference conference,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) => new()
    {
        Id = "conference",
        ConferenceId = conference.Id,
        Type = "conference",
        SchemaVersion = 1,
        Slug = conference.Slug,
        Title = conference.Title,
        Description = conference.Description,
        LifecycleState = conference.LifecycleState.ToString(),
        Visibility = conference.Visibility.ToString(),
        TimeZoneId = conference.TimeZoneId,
        StartsAtUtc = conference.StartsAtUtc,
        EndsAtUtc = conference.EndsAtUtc,
        CfpState = conference.CfpState.ToString(),
        CfpOpensAtUtc = conference.CfpOpensAtUtc,
        CfpClosesAtUtc = conference.CfpClosesAtUtc,
        PublicShowcaseEnabled = conference.PublicShowcaseEnabled,
        OwnerRosterRevision = conference.OwnerRosterRevision,
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = updatedAtUtc
    };

    private async Task UpdateDirectoryAsync(
        string state,
        Conference conference,
        CancellationToken cancellationToken)
    {
        var response = await _conferenceDirectory.ReadItemAsync<ConferenceDirectoryEntry>(
            "entry",
            new PartitionKey(conference.Slug),
            cancellationToken: cancellationToken);
        var directory = response.Resource;
        if (directory.ConferenceId != conference.Id)
        {
            throw new RequestConflictException("The public slug is mapped to another conference.");
        }

        directory.State = state;
        directory.Title = conference.Title;
        directory.StartsAtUtc = conference.StartsAtUtc;
        directory.TimeZoneId = conference.TimeZoneId;
        directory.CfpState = conference.CfpState.ToString();
        directory.CfpOpensAtUtc = conference.CfpOpensAtUtc;
        directory.CfpClosesAtUtc = conference.CfpClosesAtUtc;
        try
        {
            await _conferenceDirectory.ReplaceItemAsync(
                directory,
                "entry",
                new PartitionKey(conference.Slug),
                new ItemRequestOptions { IfMatchEtag = response.ETag },
                cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new RequestConflictException("The public conference directory changed. Reload before saving.");
        }
    }

    private static bool IsPublic(ConferenceDataDocument document) =>
        document.LifecycleState == ConferenceLifecycleState.Active.ToString() &&
        document.Visibility == ConferenceVisibility.Public.ToString();

    private static Conference Map(ConferenceDataDocument document)
    {
        var conference = Conference.Create(
            document.ConferenceId,
            document.Slug,
            document.Title,
            document.Description,
            document.TimeZoneId,
            document.StartsAtUtc,
            document.EndsAtUtc,
            document.CfpOpensAtUtc,
            document.CfpClosesAtUtc);
        return conference with
        {
            LifecycleState = ParseEnum<ConferenceLifecycleState>(document.LifecycleState),
            Visibility = ParseEnum<ConferenceVisibility>(document.Visibility),
            CfpState = ParseEnum<CfpPublicationState>(document.CfpState),
            PublicShowcaseEnabled = document.PublicShowcaseEnabled,
            OwnerRosterRevision = document.OwnerRosterRevision,
            UpdatedAtUtc = document.UpdatedAtUtc
        };
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new JsonSerializationException($"Invalid {typeof(TEnum).Name} value in conference data.");

    private static MembershipDocument ToDocument(ConferenceMembership membership) => new()
    {
        Id = $"membership:{membership.UserId}",
        ConferenceId = membership.ConferenceId,
        Type = "conferenceMembership",
        UserId = membership.UserId,
        Role = membership.Role.ToString(),
        State = membership.IsActive ? "Active" : "Inactive"
    };

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

    private sealed class ConferenceDirectoryEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;

        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("startsAtUtc")]
        public DateTimeOffset StartsAtUtc { get; set; }

        [JsonProperty("timeZoneId")]
        public string TimeZoneId { get; set; } = string.Empty;

        [JsonProperty("cfpState")]
        public string CfpState { get; set; } = string.Empty;

        [JsonProperty("cfpOpensAtUtc")]
        public DateTimeOffset CfpOpensAtUtc { get; set; }

        [JsonProperty("cfpClosesAtUtc")]
        public DateTimeOffset CfpClosesAtUtc { get; set; }

        [JsonProperty("operationId")]
        public string OperationId { get; set; } = string.Empty;

        [JsonProperty("requestHash")]
        public string RequestHash { get; set; } = string.Empty;
    }

    private sealed class ConferenceDataDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }
        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        [JsonProperty("lifecycleState")]
        public string LifecycleState { get; set; } = string.Empty;
        [JsonProperty("visibility")]
        public string Visibility { get; set; } = string.Empty;
        [JsonProperty("timeZoneId")]
        public string TimeZoneId { get; set; } = string.Empty;
        [JsonProperty("startsAtUtc")]
        public DateTimeOffset StartsAtUtc { get; set; }
        [JsonProperty("endsAtUtc")]
        public DateTimeOffset EndsAtUtc { get; set; }
        [JsonProperty("cfpState")]
        public string CfpState { get; set; } = string.Empty;
        [JsonProperty("cfpOpensAtUtc")]
        public DateTimeOffset CfpOpensAtUtc { get; set; }
        [JsonProperty("cfpClosesAtUtc")]
        public DateTimeOffset CfpClosesAtUtc { get; set; }
        [JsonProperty("publicShowcaseEnabled")]
        public bool PublicShowcaseEnabled { get; set; }
        [JsonProperty("ownerRosterRevision")]
        public int OwnerRosterRevision { get; set; }
        [JsonProperty("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
        [JsonProperty("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed class MembershipDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonProperty("role")]
        public string Role { get; set; } = string.Empty;
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;
    }

    private sealed class UserProfileReference
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("userId")]
        public string UserId { get; set; } = string.Empty;
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

    private sealed class OperationMarker
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("operationId")]
        public string OperationId { get; set; } = string.Empty;
        [JsonProperty("requestHash")]
        public string RequestHash { get; set; } = string.Empty;
    }
}
