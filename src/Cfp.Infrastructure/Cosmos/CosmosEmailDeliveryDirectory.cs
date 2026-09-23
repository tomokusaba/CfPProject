using System.Net;
using Cfp.Application.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosEmailDeliveryDirectory(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IEmailDeliveryDirectory
{
    private readonly Container _directory =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "emailDeliveryDirectory");

    public async Task LinkAsync(
        string providerMessageId,
        string conferenceId,
        string outboxId,
        CancellationToken cancellationToken)
    {
        var link = new DeliveryLinkDocument
        {
            Id = providerMessageId,
            ProviderMessageId = providerMessageId,
            ConferenceId = conferenceId,
            OutboxId = outboxId
        };
        try
        {
            await _directory.CreateItemAsync(
                link,
                new PartitionKey(providerMessageId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await ReadLinkAsync(providerMessageId, cancellationToken);
            if (existing is null ||
                existing.ConferenceId != conferenceId ||
                existing.OutboxId != outboxId)
            {
                throw new RequestConflictException("Provider message ID is already linked to another outbox item.");
            }
        }
    }

    public async Task<EmailDeliveryLink?> ResolveAsync(
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        var link = await ReadLinkAsync(providerMessageId, cancellationToken);
        return link is null ? null : new EmailDeliveryLink(link.ConferenceId, link.OutboxId);
    }

    public async Task<bool> RecordEventReceivedAsync(
        string providerMessageId,
        string eventId,
        string eventStatus,
        CancellationToken cancellationToken)
    {
        var journal = new DeliveryEventDocument
        {
            Id = $"event:{eventId}",
            ProviderMessageId = providerMessageId,
            EventId = eventId,
            DeliveryStatus = eventStatus,
            State = "Received"
        };
        try
        {
            await _directory.CreateItemAsync(
                journal,
                new PartitionKey(providerMessageId),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await ReadEventAsync(providerMessageId, eventId, cancellationToken);
            if (existing is null)
            {
                throw new InvalidOperationException("Delivery event journal entry could not be read after a conflict.");
            }

            if (!string.Equals(existing.DeliveryStatus, eventStatus, StringComparison.Ordinal))
            {
                throw new RequestConflictException("An Event Grid event ID was reused with a different status.");
            }

            return existing.State == "Received";
        }
    }

    public async Task MarkEventAppliedAsync(
        string providerMessageId,
        string eventId,
        CancellationToken cancellationToken)
    {
        var current = await ReadEventWithEtagAsync(providerMessageId, eventId, cancellationToken)
            ?? throw new InvalidOperationException("Delivery event journal entry was not found.");
        if (current.Value.State == "Applied")
        {
            return;
        }

        var updated = current.Value with { State = "Applied" };
        try
        {
            await _directory.ReplaceItemAsync(
                updated,
                updated.Id,
                new PartitionKey(providerMessageId),
                new ItemRequestOptions { IfMatchEtag = current.ETag },
                cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            var latest = await ReadEventAsync(providerMessageId, eventId, cancellationToken);
            if (latest?.State != "Applied")
            {
                throw new RequestConflictException("Delivery event journal entry changed while being applied.");
            }
        }
    }

    public async Task MarkEventAnomalyAsync(
        string providerMessageId,
        string eventId,
        string reason,
        CancellationToken cancellationToken)
    {
        var current = await ReadEventWithEtagAsync(providerMessageId, eventId, cancellationToken)
            ?? throw new InvalidOperationException("Delivery event journal entry was not found.");
        if (current.Value.State is "Applied" or "Anomaly")
        {
            return;
        }

        var updated = current.Value with
        {
            State = "Anomaly",
            AnomalyReason = reason
        };
        try
        {
            await _directory.ReplaceItemAsync(
                updated,
                updated.Id,
                new PartitionKey(providerMessageId),
                new ItemRequestOptions { IfMatchEtag = current.ETag },
                cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            var latest = await ReadEventAsync(providerMessageId, eventId, cancellationToken);
            if (latest?.State != "Anomaly" && latest?.State != "Applied")
            {
                throw new RequestConflictException("Delivery event journal entry changed while recording anomaly.");
            }
        }
    }

    private async Task<DeliveryLinkDocument?> ReadLinkAsync(
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _directory.ReadItemAsync<DeliveryLinkDocument>(
                providerMessageId,
                new PartitionKey(providerMessageId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<DeliveryEventDocument?> ReadEventAsync(
        string providerMessageId,
        string eventId,
        CancellationToken cancellationToken)
    {
        var result = await ReadEventWithEtagAsync(providerMessageId, eventId, cancellationToken);
        return result?.Value;
    }

    private async Task<Versioned<DeliveryEventDocument>?> ReadEventWithEtagAsync(
        string providerMessageId,
        string eventId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _directory.ReadItemAsync<DeliveryEventDocument>(
                $"event:{eventId}",
                new PartitionKey(providerMessageId),
                cancellationToken: cancellationToken);
            return new Versioned<DeliveryEventDocument>(response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed class DeliveryLinkDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("providerMessageId")]
        public string ProviderMessageId { get; set; } = string.Empty;
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;
        [JsonProperty("outboxId")]
        public string OutboxId { get; set; } = string.Empty;
    }

    private sealed record DeliveryEventDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("providerMessageId")]
        public string ProviderMessageId { get; set; } = string.Empty;
        [JsonProperty("eventId")]
        public string EventId { get; set; } = string.Empty;
        [JsonProperty("deliveryStatus")]
        public string DeliveryStatus { get; set; } = string.Empty;
        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;
        [JsonProperty("anomalyReason")]
        public string? AnomalyReason { get; set; }
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
    }
}
