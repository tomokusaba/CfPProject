using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Domain.Proposals;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosProposalWorkflowStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IProposalWorkflowStore
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<Versioned<Proposal>> CreateDraftAsync(
        Proposal proposal,
        AuditEvent auditEvent,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var marker = new ProposalOperationDocument
        {
            Id = $"operation:{idempotencyKey}",
            ConferenceId = proposal.ConferenceId,
            Type = "operation",
            OperationId = idempotencyKey,
            RequestHash = requestHash,
            ResultId = proposal.Id
        };
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(proposal.ConferenceId))
            .CreateItem(ToDocument(proposal))
            .CreateItem(ToDocument(auditEvent))
            .CreateItem(marker);
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var existingMarker = await FindOperationAsync(
                proposal.ConferenceId,
                idempotencyKey,
                cancellationToken);
            if (existingMarker is null ||
                !string.Equals(existingMarker.RequestHash, requestHash, StringComparison.Ordinal) ||
                !string.Equals(existingMarker.ResultId, proposal.Id, StringComparison.Ordinal))
            {
                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
                {
                    throw new RequestConflictException("This proposal operation conflicts with existing data.");
                }

                throw new CosmosException(
                    $"Creating proposal draft failed with Cosmos DB status {(int)response.StatusCode}.",
                    response.StatusCode,
                    0,
                    response.ActivityId,
                    response.RequestCharge);
            }
        }

        var saved = await GetOwnedAsync(
            proposal.ConferenceId,
            proposal.Id,
            proposal.OwnerUserId,
            cancellationToken);
        return saved ?? throw new InvalidOperationException("Created proposal could not be read back.");
    }

    public async Task<Versioned<Proposal>?> GetOwnedAsync(
        string conferenceId,
        string proposalId,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var proposal = await GetByIdAsync(conferenceId, proposalId, cancellationToken);
        return proposal is not null &&
               string.Equals(proposal.Value.OwnerUserId, ownerUserId, StringComparison.Ordinal)
            ? proposal
            : null;
    }

    public async Task<Versioned<Proposal>?> GetByIdAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<ProposalDocument>(
                $"proposal:{proposalId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<Proposal>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Versioned<Proposal>> UpdateAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        await ReplaceWithAuditAsync(proposal, expectedEtag, auditEvent, null, cancellationToken);

    public async Task<Versioned<Proposal>> SubmitAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var outbox = new EmailOutboxDocument
        {
            Id = $"outbox:proposal-received:{proposal.Id}:{proposal.OwnerUserId}",
            ConferenceId = proposal.ConferenceId,
            Type = "emailOutbox",
            RecipientUserId = proposal.OwnerUserId,
            Category = "Transactional",
            TemplateId = "proposal-received",
            Status = "Ready",
            CreatedAtUtc = auditEvent.OccurredAtUtc
        };
        return await ReplaceWithAuditAsync(
            proposal,
            expectedEtag,
            auditEvent,
            outbox,
            cancellationToken);
    }

    public async Task<Versioned<Proposal>> WithdrawAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        await ReplaceWithAuditAsync(proposal, expectedEtag, auditEvent, null, cancellationToken);

    public Task<Versioned<Proposal>> SetPublicationConsentAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        ReplaceWithAuditAsync(proposal, expectedEtag, auditEvent, null, cancellationToken);

    public Task<Versioned<Proposal>> SetPublicationStateAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        ReplaceWithAuditAsync(proposal, expectedEtag, auditEvent, null, cancellationToken);

    public async Task<ProposalPage> ListByOwnerAsync(
        string ownerUserId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type AND c.ownerUserId = @ownerUserId ORDER BY c.updatedAtUtc DESC")
            .WithParameter("@type", "proposal")
            .WithParameter("@ownerUserId", ownerUserId);
        using var iterator = _conferenceData.GetItemQueryIterator<ProposalDocument>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
        {
            return new ProposalPage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        return new ProposalPage(
            response.Resource
                .Select(document => new Versioned<Proposal>(Map(document), RequireETag(document.ETag)))
                .ToArray(),
            response.ContinuationToken);
    }

    public async Task<ProposalPage> ListByConferenceAsync(
        string conferenceId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type ORDER BY c.createdAtUtc DESC")
            .WithParameter("@type", "proposal");
        using var iterator = _conferenceData.GetItemQueryIterator<ProposalDocument>(
            query,
            continuationToken,
            new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = pageSize
            });
        if (!iterator.HasMoreResults)
        {
            return new ProposalPage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        return new ProposalPage(
            response.Resource
                .Select(document => new Versioned<Proposal>(Map(document), RequireETag(document.ETag)))
                .ToArray(),
            response.ContinuationToken);
    }

    private async Task<Versioned<Proposal>> ReplaceWithAuditAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        EmailOutboxDocument? outbox,
        CancellationToken cancellationToken)
    {
        var replacement = ToDocument(proposal);
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(proposal.ConferenceId))
            .ReplaceItem(
                replacement.Id,
                replacement,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag })
            .CreateItem(ToDocument(auditEvent));
        if (outbox is not null)
        {
            batch.CreateItem(outbox);
        }

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                throw new RequestConflictException("The proposal changed. Reload it before trying again.");
            }

            throw new CosmosException(
                $"Saving proposal failed with Cosmos DB status {(int)response.StatusCode}.",
                response.StatusCode,
                0,
                response.ActivityId,
                response.RequestCharge);
        }

        var saved = await GetOwnedAsync(
            proposal.ConferenceId,
            proposal.Id,
            proposal.OwnerUserId,
            cancellationToken);
        return saved ?? throw new InvalidOperationException("Saved proposal could not be read back.");
    }

    private async Task<ProposalOperationDocument?> FindOperationAsync(
        string conferenceId,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<ProposalOperationDocument>(
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

    private static ProposalDocument ToDocument(Proposal proposal) => new()
    {
        Id = $"proposal:{proposal.Id}",
        ConferenceId = proposal.ConferenceId,
        Type = "proposal",
        ProposalId = proposal.Id,
        OwnerUserId = proposal.OwnerUserId,
        ProposalTypeId = proposal.ProposalTypeId,
        FormVersion = proposal.FormVersion,
        Answers = new Dictionary<string, string>(proposal.Answers, StringComparer.Ordinal),
        Status = proposal.Status.ToString(),
        PublicationState = proposal.PublicationState.ToString(),
        PublicationConsentConfirmed = proposal.PublicationConsentConfirmed,
        PublicationConsentUserId = proposal.PublicationConsentUserId,
        PublicationConsentAtUtc = proposal.PublicationConsentAtUtc,
        PublicationConsentStatementVersion = proposal.PublicationConsentStatementVersion,
        SubmittedAtUtc = proposal.SubmittedAtUtc,
        CreatedAtUtc = proposal.CreatedAtUtc,
        UpdatedAtUtc = proposal.UpdatedAtUtc,
        DecisionReason = proposal.DecisionReason
    };

    private static Proposal Map(ProposalDocument document)
    {
        if (!Enum.TryParse<ProposalStatus>(document.Status, ignoreCase: false, out var status) ||
            !Enum.TryParse<ProposalPublicationState>(document.PublicationState, ignoreCase: false, out var publicationState))
        {
            throw new JsonSerializationException("Invalid proposal state in stored proposal data.");
        }

        return new Proposal
        {
            Id = document.ProposalId,
            ConferenceId = document.ConferenceId,
            OwnerUserId = document.OwnerUserId,
            ProposalTypeId = document.ProposalTypeId,
            FormVersion = document.FormVersion,
            Answers = document.Answers,
            Status = status,
            PublicationState = publicationState,
            PublicationConsentConfirmed = document.PublicationConsentConfirmed,
            PublicationConsentUserId = document.PublicationConsentUserId,
            PublicationConsentAtUtc = document.PublicationConsentAtUtc,
            PublicationConsentStatementVersion = document.PublicationConsentStatementVersion,
            SubmittedAtUtc = document.SubmittedAtUtc,
            CreatedAtUtc = document.CreatedAtUtc,
            UpdatedAtUtc = document.UpdatedAtUtc,
            DecisionReason = document.DecisionReason
        };
    }

    private static string RequireETag(string? etag) =>
        !string.IsNullOrWhiteSpace(etag)
            ? etag
            : throw new JsonSerializationException("Proposal query result is missing its ETag.");

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

    private sealed class ProposalDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("proposalId")]
        public string ProposalId { get; set; } = string.Empty;
        [JsonProperty("ownerUserId")]
        public string OwnerUserId { get; set; } = string.Empty;
        [JsonProperty("proposalTypeId")]
        public string ProposalTypeId { get; set; } = string.Empty;
        [JsonProperty("formVersion")]
        public int FormVersion { get; set; }
        [JsonProperty("answers")]
        public Dictionary<string, string> Answers { get; set; } = new(StringComparer.Ordinal);
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;
        [JsonProperty("publicationState")]
        public string PublicationState { get; set; } = "Hidden";
        [JsonProperty("publicationConsentConfirmed")]
        public bool PublicationConsentConfirmed { get; set; }
        [JsonProperty("publicationConsentUserId")]
        public string? PublicationConsentUserId { get; set; }
        [JsonProperty("publicationConsentAtUtc")]
        public DateTimeOffset? PublicationConsentAtUtc { get; set; }
        [JsonProperty("publicationConsentStatementVersion")]
        public string? PublicationConsentStatementVersion { get; set; }
        [JsonProperty("submittedAtUtc")]
        public DateTimeOffset? SubmittedAtUtc { get; set; }
        [JsonProperty("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
        [JsonProperty("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
        [JsonProperty("decisionReason")]
        public string? DecisionReason { get; set; }
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
    }

    private sealed class ProposalOperationDocument
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
        [JsonProperty("resultId")]
        public string ResultId { get; set; } = string.Empty;
    }

    private sealed class EmailOutboxDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("recipientUserId")]
        public string RecipientUserId { get; set; } = string.Empty;
        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty;
        [JsonProperty("templateId")]
        public string TemplateId { get; set; } = string.Empty;
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;
        [JsonProperty("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
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
