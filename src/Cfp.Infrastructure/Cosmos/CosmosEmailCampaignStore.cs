using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Domain.Notifications;
using Cfp.Domain.Proposals;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosEmailCampaignStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IEmailCampaignStore
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<Versioned<EmailCampaign>?> GetAsync(
        string conferenceId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<EmailCampaignDocument>(
                $"emailCampaign:{campaignId}",
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<EmailCampaign>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Versioned<EmailCampaign>> CreatePreviewAsync(
        EmailCampaign campaign,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(campaign.ConferenceId))
            .CreateItem(ToDocument(campaign))
            .CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var existing = await GetAsync(campaign.ConferenceId, campaign.Id, cancellationToken);
                if (existing is not null &&
                    existing.Value.CreatedByUserId == campaign.CreatedByUserId &&
                    existing.Value.RequestHash == campaign.RequestHash)
                {
                    return existing;
                }

                throw new RequestConflictException("Email preview idempotency key conflicts with a different request.");
            }

            throw ToCosmosException("Creating email preview", response);
        }

        return await GetAsync(campaign.ConferenceId, campaign.Id, cancellationToken)
            ?? throw new InvalidOperationException("Created email preview could not be read back.");
    }

    public async Task<Versioned<EmailCampaign>> ConfirmAndEnqueueAsync(
        EmailCampaign campaign,
        string expectedEtag,
        IReadOnlyList<EmailOutboxItem> outboxItems,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        if (outboxItems.Count > 50)
        {
            throw new ArgumentException("An email campaign can enqueue at most 50 recipients in one operation.");
        }

        var current = await GetAsync(campaign.ConferenceId, campaign.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Email campaign preview was not found.");
        if (current.Value.State == EmailCampaignState.Ready &&
            current.Value.ConfirmedOperationId == campaign.ConfirmedOperationId &&
            string.Equals(
                current.Value.ConfirmationReason,
                campaign.ConfirmationReason,
                StringComparison.Ordinal))
        {
            return current;
        }

        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("Email preview changed. Reload before sending.");
        }

        var batch = _conferenceData
            .CreateTransactionalBatch(new PartitionKey(campaign.ConferenceId))
            .ReplaceItem(
                $"emailCampaign:{campaign.Id}",
                ToDocument(campaign),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = expectedEtag });
        foreach (var outboxItem in outboxItems)
        {
            batch.CreateItem(ToDocument(outboxItem));
        }

        batch.CreateItem(ToDocument(auditEvent));
        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                var latest = await GetAsync(campaign.ConferenceId, campaign.Id, cancellationToken);
                if (latest is not null &&
                    latest.Value.State == EmailCampaignState.Ready &&
                    latest.Value.ConfirmedOperationId == campaign.ConfirmedOperationId &&
                    string.Equals(
                        latest.Value.ConfirmationReason,
                        campaign.ConfirmationReason,
                        StringComparison.Ordinal))
                {
                    return latest;
                }

                throw new RequestConflictException("Email preview changed. Reload before sending.");
            }

            throw ToCosmosException("Sending email campaign", response);
        }

        return await GetAsync(campaign.ConferenceId, campaign.Id, cancellationToken)
            ?? throw new InvalidOperationException("Sent email campaign could not be read back.");
    }

    private static EmailCampaignDocument ToDocument(EmailCampaign campaign) => new()
    {
        Id = $"emailCampaign:{campaign.Id}",
        ConferenceId = campaign.ConferenceId,
        Type = "emailCampaign",
        CampaignId = campaign.Id,
        CreatedByUserId = campaign.CreatedByUserId,
        TargetProposalStatuses = campaign.TargetProposalStatuses.Select(status => status.ToString()).ToList(),
        RecipientUserIds = campaign.RecipientUserIds.ToList(),
        SenderAddress = campaign.SenderAddress,
        Subject = campaign.Subject,
        PlainTextContent = campaign.PlainTextContent,
        ExcludedRecipientCount = campaign.ExcludedRecipientCount,
        RequestHash = campaign.RequestHash,
        State = campaign.State.ToString(),
        CreatedAtUtc = campaign.CreatedAtUtc,
        ExpiresAtUtc = campaign.ExpiresAtUtc,
        ConfirmedAtUtc = campaign.ConfirmedAtUtc,
        ConfirmedOperationId = campaign.ConfirmedOperationId,
        ConfirmationReason = campaign.ConfirmationReason
    };

    private static EmailOutboxDocument ToDocument(EmailOutboxItem item) => new()
    {
        Id = item.Id,
        ConferenceId = item.ConferenceId,
        Type = "emailOutbox",
        RecipientUserId = item.RecipientUserId,
        Category = item.Category.ToString(),
        TemplateId = item.TemplateId,
        Status = item.Status.ToString(),
        StatusReason = item.StatusReason,
        Subject = item.Subject,
        PlainTextContent = item.PlainTextContent,
        SenderAddress = item.SenderAddress,
        CreatedAtUtc = item.CreatedAtUtc,
        UpdatedAtUtc = item.UpdatedAtUtc
    };

    private static EmailCampaign Map(EmailCampaignDocument document)
    {
        if (!Enum.TryParse<EmailCampaignState>(document.State, ignoreCase: false, out var state))
        {
            throw new JsonSerializationException("Invalid email campaign state in storage.");
        }

        return new EmailCampaign(
            document.CampaignId,
            document.ConferenceId,
            document.CreatedByUserId,
            document.TargetProposalStatuses.Select(status =>
            {
                if (!Enum.TryParse<ProposalStatus>(status, ignoreCase: false, out var parsed))
                {
                    throw new JsonSerializationException("Email campaign contains an invalid target proposal status.");
                }

                return parsed;
            }).ToArray(),
            document.RecipientUserIds,
            document.SenderAddress,
            document.Subject,
            document.PlainTextContent,
            document.ExcludedRecipientCount,
            document.RequestHash,
            state,
            document.CreatedAtUtc,
            document.ExpiresAtUtc,
            document.ConfirmedAtUtc,
            document.ConfirmedOperationId,
            document.ConfirmationReason);
    }

    private static CosmosException ToCosmosException(
        string operation,
        TransactionalBatchResponse response) =>
        new(
            $"{operation} failed with Cosmos DB status {(int)response.StatusCode}.",
            response.StatusCode,
            0,
            response.ActivityId,
            response.RequestCharge);

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

    private sealed class EmailCampaignDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("campaignId")]
        public string CampaignId { get; set; } = string.Empty;
        [JsonProperty("createdByUserId")]
        public string CreatedByUserId { get; set; } = string.Empty;
        [JsonProperty("targetProposalStatuses")]
        public List<string> TargetProposalStatuses { get; set; } = [];
        [JsonProperty("recipientUserIds")]
        public List<string> RecipientUserIds { get; set; } = [];
        [JsonProperty("senderAddress")]
        public string SenderAddress { get; set; } = string.Empty;
        [JsonProperty("subject")]
        public string Subject { get; set; } = string.Empty;
        [JsonProperty("plainTextContent")]
        public string PlainTextContent { get; set; } = string.Empty;
        [JsonProperty("excludedRecipientCount")]
        public int ExcludedRecipientCount { get; set; }
        [JsonProperty("requestHash")]
        public string RequestHash { get; set; } = string.Empty;
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;
        [JsonProperty("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
        [JsonProperty("expiresAtUtc")]
        public DateTimeOffset ExpiresAtUtc { get; set; }
        [JsonProperty("confirmedAtUtc")]
        public DateTimeOffset? ConfirmedAtUtc { get; set; }
        [JsonProperty("confirmedOperationId")]
        public string? ConfirmedOperationId { get; set; }
        [JsonProperty("confirmationReason")]
        public string? ConfirmationReason { get; set; }
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
        [JsonProperty("statusReason")]
        public string? StatusReason { get; set; }
        [JsonProperty("subject", NullValueHandling = NullValueHandling.Ignore)]
        public string? Subject { get; set; }
        [JsonProperty("plainTextContent", NullValueHandling = NullValueHandling.Ignore)]
        public string? PlainTextContent { get; set; }
        [JsonProperty("senderAddress", NullValueHandling = NullValueHandling.Ignore)]
        public string? SenderAddress { get; set; }
        [JsonProperty("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; set; }
        [JsonProperty("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; }
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
