using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.ProposalTypes;
using Cfp.Domain.Proposals;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosProposalTypeStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IProposalTypeStore
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<Versioned<ProposalType>?> GetAsync(
        string conferenceId,
        string proposalTypeId,
        CancellationToken cancellationToken)
    {
        ProposalTypeCurrentDocument current;
        string etag;
        try
        {
            var currentResponse = await _conferenceData.ReadItemAsync<ProposalTypeCurrentDocument>(
                $"proposalType:{proposalTypeId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            current = currentResponse.Resource;
            etag = currentResponse.ETag;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var version = await ReadVersionAsync(conferenceId, proposalTypeId, current.FormVersion, cancellationToken);
        return new Versioned<ProposalType>(Map(current, version), etag);
    }

    public async Task<IReadOnlyList<Versioned<ProposalType>>> ListAsync(
        string conferenceId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type")
            .WithParameter("@type", "proposalType");
        using var iterator = _conferenceData.GetItemQueryIterator<ProposalTypeCurrentDocument>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = 50
            });

        var results = new List<Versioned<ProposalType>>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var current in response.Resource)
            {
                var version = await ReadVersionAsync(
                    conferenceId,
                    current.ProposalTypeId,
                    current.FormVersion,
                    cancellationToken);
                results.Add(new Versioned<ProposalType>(
                    Map(current, version),
                    RequireETag(current.ETag)));
            }
        }

        return results;
    }

    public async Task<ProposalType?> GetVersionAsync(
        string conferenceId,
        string proposalTypeId,
        int formVersion,
        CancellationToken cancellationToken)
    {
        ProposalTypeCurrentDocument current;
        try
        {
            var response = await _conferenceData.ReadItemAsync<ProposalTypeCurrentDocument>(
                $"proposalType:{proposalTypeId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            current = response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        try
        {
            var version = await ReadVersionAsync(conferenceId, proposalTypeId, formVersion, cancellationToken);
            return Map(current, version);
        }
        catch (JsonSerializationException)
        {
            return null;
        }
    }

    public async Task<Versioned<ProposalType>> SaveNewVersionAsync(
        ProposalType proposalType,
        string? expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var current = new ProposalTypeCurrentDocument
        {
            Id = $"proposalType:{proposalType.Id}",
            ConferenceId = proposalType.ConferenceId,
            Type = "proposalType",
            ProposalTypeId = proposalType.Id,
            Name = proposalType.Name,
            Description = proposalType.Description,
            DurationMinutes = proposalType.DurationMinutes,
            FormVersion = proposalType.FormVersion,
            IsAcceptingSubmissions = proposalType.IsAcceptingSubmissions,
            IsPublic = proposalType.IsPublic,
            UpdatedAtUtc = auditEvent.OccurredAtUtc,
            UpdatedBy = auditEvent.ActorUserId
        };
        var version = new ProposalTypeVersionDocument
        {
            Id = $"proposalTypeVersion:{proposalType.Id}:{proposalType.FormVersion}",
            ConferenceId = proposalType.ConferenceId,
            Type = "proposalTypeVersion",
            ProposalTypeId = proposalType.Id,
            FormVersion = proposalType.FormVersion,
            Fields = proposalType.Fields.Select(ToDocument).ToList(),
            CreatedAtUtc = auditEvent.OccurredAtUtc,
            CreatedBy = auditEvent.ActorUserId
        };
        var batch = _conferenceData.CreateTransactionalBatch(new PartitionKey(proposalType.ConferenceId));
        batch.CreateItem(version);
        if (expectedEtag is null)
        {
            batch.CreateItem(current);
        }
        else
        {
            batch.ReplaceItem(
                current.Id,
                current,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag });
        }

        batch.CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The proposal type changed. Reload it before saving.");
            }

            throw new CosmosException(
                $"Saving proposal type failed with Cosmos DB status {(int)response.StatusCode}.",
                response.StatusCode,
                0,
                response.ActivityId,
                response.RequestCharge);
        }

        var saved = await GetAsync(
            proposalType.ConferenceId,
            proposalType.Id,
            cancellationToken);
        return saved ?? throw new InvalidOperationException("Saved proposal type could not be read back.");
    }

    private async Task<ProposalTypeVersionDocument> ReadVersionAsync(
        string conferenceId,
        string proposalTypeId,
        int formVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<ProposalTypeVersionDocument>(
                $"proposalTypeVersion:{proposalTypeId}:{formVersion}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new JsonSerializationException("Proposal type references a missing immutable form version.");
        }
    }

    private static ProposalType Map(
        ProposalTypeCurrentDocument current,
        ProposalTypeVersionDocument version)
    {
        var fields = version.Fields.Select(field =>
        {
            if (!Enum.TryParse<FormFieldKind>(field.Kind, ignoreCase: false, out var kind))
            {
                throw new JsonSerializationException("Invalid form field kind in stored proposal type.");
            }

            return new ProposalFormField(
                field.Id,
                field.Label,
                kind,
                field.IsRequired,
                field.MaximumLength,
                field.Options);
        }).ToArray();

        return new ProposalType
        {
            Id = current.ProposalTypeId,
            ConferenceId = current.ConferenceId,
            Name = current.Name,
            Description = current.Description,
            DurationMinutes = current.DurationMinutes,
            FormVersion = version.FormVersion,
            IsAcceptingSubmissions = current.IsAcceptingSubmissions,
            IsPublic = current.IsPublic,
            Fields = fields
        };
    }

    private static ProposalFormFieldDocument ToDocument(ProposalFormField field) => new()
    {
        Id = field.Id,
        Label = field.Label,
        Kind = field.Kind.ToString(),
        IsRequired = field.IsRequired,
        MaximumLength = field.MaximumLength,
        Options = field.Options?.ToList()
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

    private static string RequireETag(string? etag) =>
        !string.IsNullOrWhiteSpace(etag)
            ? etag
            : throw new JsonSerializationException("Proposal type query result is missing its ETag.");

    private sealed class ProposalTypeCurrentDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("proposalTypeId")]
        public string ProposalTypeId { get; set; } = string.Empty;
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        [JsonProperty("durationMinutes")]
        public int DurationMinutes { get; set; }
        [JsonProperty("formVersion")]
        public int FormVersion { get; set; }
        [JsonProperty("isAcceptingSubmissions")]
        public bool IsAcceptingSubmissions { get; set; }
        [JsonProperty("isPublic")]
        public bool IsPublic { get; set; }
        [JsonProperty("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
        [JsonProperty("updatedBy")]
        public string UpdatedBy { get; set; } = string.Empty;
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
    }

    private sealed class ProposalTypeVersionDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("proposalTypeId")]
        public string ProposalTypeId { get; set; } = string.Empty;
        [JsonProperty("formVersion")]
        public int FormVersion { get; set; }
        [JsonProperty("fields")]
        public List<ProposalFormFieldDocument> Fields { get; set; } = [];
        [JsonProperty("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
        [JsonProperty("createdBy")]
        public string CreatedBy { get; set; } = string.Empty;
    }

    private sealed class ProposalFormFieldDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("label")]
        public string Label { get; set; } = string.Empty;
        [JsonProperty("kind")]
        public string Kind { get; set; } = string.Empty;
        [JsonProperty("isRequired")]
        public bool IsRequired { get; set; }
        [JsonProperty("maximumLength")]
        public int? MaximumLength { get; set; }
        [JsonProperty("options")]
        public List<string>? Options { get; set; }
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
