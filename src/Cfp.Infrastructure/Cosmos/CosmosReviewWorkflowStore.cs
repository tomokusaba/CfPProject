using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Domain.Proposals;
using Cfp.Domain.Reviews;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosReviewWorkflowStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IReviewWorkflowStore
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<Versioned<Proposal>?> GetProposalAsync(
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

    public async Task<Versioned<ReviewerAssignment>?> GetAssignmentAsync(
        string conferenceId,
        string proposalId,
        string reviewerUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<AssignmentDocument>(
                $"assignment:{proposalId}:{reviewerUserId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<ReviewerAssignment>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ReviewerAssignment> AssignAsync(
        ReviewerAssignment assignment,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var proposalResponse = await ReadProposalAsync(
            assignment.ConferenceId,
            assignment.ProposalId,
            cancellationToken);
        var proposal = Map(proposalResponse.Resource);
        if (proposal.Status is not (ProposalStatus.Submitted or ProposalStatus.UnderReview))
        {
            throw new InvalidOperationException("Only submitted proposals can be assigned for review.");
        }

        var assignmentDocument = ToDocument(assignment);
        var audit = CreateAudit(
            assignment.ConferenceId,
            actorUserId,
            "ReviewerAssigned",
            assignment.ProposalId,
            $"Reviewer assigned. Reason: {reason}",
            assignment.AssignedAtUtc);
        var batch = _conferenceData.CreateTransactionalBatch(new PartitionKey(assignment.ConferenceId));
        if (proposal.Status == ProposalStatus.Submitted)
        {
            batch.ReplaceItem(
                proposalResponse.Resource.Id,
                ToDocument(proposal.MarkUnderReview(assignment.AssignedAtUtc)),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = proposalResponse.ETag });
        }

        batch.CreateItem(assignmentDocument)
            .CreateItem(audit);
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The proposal assignment changed. Reload and try again.");
            }

            throw ToCosmosException("Assigning reviewer", response);
        }

        var stored = await GetAssignmentAsync(
            assignment.ConferenceId,
            assignment.ProposalId,
            assignment.ReviewerUserId,
            cancellationToken);
        return stored?.Value ?? throw new InvalidOperationException("Reviewer assignment could not be read back.");
    }

    public async Task<ReviewerAssignment> DeclareConflictAsync(
        ReviewerAssignment assignment,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(assignment.ReviewerUserId, actorUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the assigned reviewer can declare a conflict.");
        }

        var current = await GetAssignmentAsync(
            assignment.ConferenceId,
            assignment.ProposalId,
            assignment.ReviewerUserId,
            cancellationToken) ?? throw new KeyNotFoundException("Review assignment was not found.");
        var updated = current.Value with { HasConflictOfInterest = true };
        var audit = CreateAudit(
            assignment.ConferenceId,
            actorUserId,
            "ReviewerConflictDeclared",
            assignment.ProposalId,
            reason,
            DateTimeOffset.UtcNow);
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(assignment.ConferenceId))
            .ReplaceItem(
                $"assignment:{assignment.ProposalId}:{assignment.ReviewerUserId}",
                ToDocument(updated),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag })
            .CreateItem(audit);
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The review assignment changed. Reload before updating.");
            }

            throw ToCosmosException("Recording reviewer conflict", response);
        }

        return updated;
    }

    public async Task<Versioned<Review>> SaveReviewAsync(
        Review review,
        string? expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var reviewId = $"review:{review.ProposalId}:{review.ReviewerUserId}:1";
        var current = await ReadReviewIfExistsAsync(review.ConferenceId, reviewId, cancellationToken);
        if (current is null && expectedEtag is not null)
        {
            throw new RequestConflictException("The review changed. Reload it before saving.");
        }

        if (current is not null &&
            (expectedEtag is null || !string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal)))
        {
            throw new RequestConflictException("The review changed. Reload it before saving.");
        }

        var document = ToDocument(review, reviewId);
        var batch = _conferenceData.CreateTransactionalBatch(new PartitionKey(review.ConferenceId));
        if (current is null)
        {
            batch.CreateItem(document);
        }
        else
        {
            batch.ReplaceItem(
                reviewId,
                document,
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag });
        }

        batch.CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The review changed. Reload it before saving.");
            }

            throw ToCosmosException("Saving review", response);
        }

        var saved = await ReadReviewIfExistsAsync(review.ConferenceId, reviewId, cancellationToken)
            ?? throw new InvalidOperationException("Saved review could not be read back.");
        return new Versioned<Review>(Map(saved.Value), saved.ETag);
    }

    public async Task<IReadOnlyList<Versioned<Review>>> ListReviewsForProposalAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type AND c.proposalId = @proposalId")
            .WithParameter("@type", "review")
            .WithParameter("@proposalId", proposalId);
        using var iterator = _conferenceData.GetItemQueryIterator<ReviewDocument>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = 100
            });
        var results = new List<Versioned<Review>>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response.Resource.Select(document =>
                new Versioned<Review>(
                    Map(document),
                    RequireETag(document.ETag))));
        }

        return results;
    }

    public async Task<Versioned<Proposal>> DecideAsync(
        Proposal proposal,
        string expectedEtag,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var current = await ReadProposalAsync(proposal.ConferenceId, proposal.Id, cancellationToken);
        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The proposal changed. Reload it before making a decision.");
        }

        var outbox = new EmailOutboxDocument
        {
            Id = $"outbox:decision:{proposal.Id}:{proposal.OwnerUserId}",
            ConferenceId = proposal.ConferenceId,
            Type = "emailOutbox",
            RecipientUserId = proposal.OwnerUserId,
            Category = "Transactional",
            TemplateId = proposal.Status == ProposalStatus.Accepted ? "proposal-accepted" : "proposal-rejected",
            Status = "Ready",
            CreatedAtUtc = auditEvent.OccurredAtUtc
        };
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(proposal.ConferenceId))
            .ReplaceItem(
                current.Resource.Id,
                ToDocument(proposal),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag })
            .CreateItem(ToDocument(auditEvent))
            .CreateItem(outbox);
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                throw new RequestConflictException("The proposal changed. Reload it before making a decision.");
            }

            throw ToCosmosException("Saving proposal decision", response);
        }

        var saved = await GetProposalAsync(proposal.ConferenceId, proposal.Id, cancellationToken);
        return saved ?? throw new InvalidOperationException("Decided proposal could not be read back.");
    }

    public async Task<ReviewTaskPage> ListMyAssignmentsAsync(
        string reviewerUserId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type AND c.reviewerUserId = @reviewer AND c.state = @state")
            .WithParameter("@type", "reviewerAssignment")
            .WithParameter("@reviewer", reviewerUserId)
            .WithParameter("@state", "Active");
        using var iterator = _conferenceData.GetItemQueryIterator<AssignmentDocument>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
        {
            return new ReviewTaskPage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        var tasks = new List<ReviewTask>();
        foreach (var assignmentDocument in response.Resource)
        {
            var assignment = Map(assignmentDocument);
            var proposalResponse = await ReadProposalAsync(
                assignment.ConferenceId,
                assignment.ProposalId,
                cancellationToken);
            var review = await ReadReviewIfExistsAsync(
                assignment.ConferenceId,
                $"review:{assignment.ProposalId}:{assignment.ReviewerUserId}:1",
                cancellationToken);
            tasks.Add(new ReviewTask(
                assignment,
                Map(proposalResponse.Resource),
                review is null ? null : Map(review.Value),
                proposalResponse.ETag,
                review?.ETag));
        }

        return new ReviewTaskPage(tasks, response.ContinuationToken);
    }

    private Task<ItemResponse<ProposalDocument>> ReadProposalAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken) =>
        _conferenceData.ReadItemAsync<ProposalDocument>(
            $"proposal:{proposalId}",
            new PartitionKey(conferenceId),
            cancellationToken: cancellationToken);

    private async Task<Versioned<ReviewDocument>?> ReadReviewIfExistsAsync(
        string conferenceId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<ReviewDocument>(
                reviewId,
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<ReviewDocument>(response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static AuditDocument CreateAudit(
        string conferenceId,
        string actorUserId,
        string operation,
        string targetId,
        string summary,
        DateTimeOffset nowUtc) => new()
    {
        Id = $"audit:{Guid.NewGuid():N}",
        ConferenceId = conferenceId,
        Type = "auditEvent",
        ActorUserId = actorUserId,
        Operation = operation,
        TargetId = targetId,
        OccurredAtUtc = nowUtc.ToUniversalTime(),
        Summary = summary
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

    private static AssignmentDocument ToDocument(ReviewerAssignment assignment) => new()
    {
        Id = $"assignment:{assignment.ProposalId}:{assignment.ReviewerUserId}",
        ConferenceId = assignment.ConferenceId,
        Type = "reviewerAssignment",
        ProposalId = assignment.ProposalId,
        ReviewerUserId = assignment.ReviewerUserId,
        AssignedAtUtc = assignment.AssignedAtUtc,
        HasConflictOfInterest = assignment.HasConflictOfInterest,
        State = "Active"
    };

    private static ReviewDocument ToDocument(Review review, string id) => new()
    {
        Id = id,
        ConferenceId = review.ConferenceId,
        Type = "review",
        ProposalId = review.ProposalId,
        ReviewerUserId = review.ReviewerUserId,
        Score = review.Score,
        Comment = review.Comment,
        HasConflictOfInterest = review.HasConflictOfInterest,
        SubmittedAtUtc = review.SubmittedAtUtc
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

    private static ReviewerAssignment Map(AssignmentDocument document)
        => new(
            document.ConferenceId,
            document.ProposalId,
            document.ReviewerUserId,
            document.AssignedAtUtc,
            document.HasConflictOfInterest);

    private static Review Map(ReviewDocument document) => new(
        document.ConferenceId,
        document.ProposalId,
        document.ReviewerUserId,
        document.Score,
        document.Comment,
        document.HasConflictOfInterest,
        document.SubmittedAtUtc);

    private static CosmosException ToCosmosException(string operation, TransactionalBatchResponse response) =>
        new(
            $"{operation} failed with Cosmos DB status {(int)response.StatusCode}.",
            response.StatusCode,
            0,
            response.ActivityId,
            response.RequestCharge);

    private static string RequireETag(string? etag) =>
        !string.IsNullOrWhiteSpace(etag)
            ? etag
            : throw new JsonSerializationException("Review query result is missing its ETag.");

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

    private sealed class AssignmentDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("proposalId")]
        public string ProposalId { get; set; } = string.Empty;
        [JsonProperty("reviewerUserId")]
        public string ReviewerUserId { get; set; } = string.Empty;
        [JsonProperty("assignedAtUtc")]
        public DateTimeOffset AssignedAtUtc { get; set; }
        [JsonProperty("hasConflictOfInterest")]
        public bool HasConflictOfInterest { get; set; }
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
    }

    private sealed class ReviewDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("proposalId")]
        public string ProposalId { get; set; } = string.Empty;
        [JsonProperty("reviewerUserId")]
        public string ReviewerUserId { get; set; } = string.Empty;
        [JsonProperty("score")]
        public int Score { get; set; }
        [JsonProperty("comment")]
        public string Comment { get; set; } = string.Empty;
        [JsonProperty("hasConflictOfInterest")]
        public bool HasConflictOfInterest { get; set; }
        [JsonProperty("submittedAtUtc")]
        public DateTimeOffset SubmittedAtUtc { get; set; }
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
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
}
