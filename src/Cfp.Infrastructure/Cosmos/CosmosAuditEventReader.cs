using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosAuditEventReader(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IAuditEventReader
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<AuditEventPage> ListAsync(
        string conferenceId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type ORDER BY c.occurredAtUtc DESC")
            .WithParameter("@type", "auditEvent");
        using var iterator = _conferenceData.GetItemQueryIterator<AuditDocument>(
            query,
            continuationToken,
            new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = pageSize
            });
        if (!iterator.HasMoreResults)
        {
            return new AuditEventPage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        return new AuditEventPage(
            response.Resource.Select(item => new AuditEvent(
                item.Id,
                item.ConferenceId,
                item.ActorUserId,
                item.Operation,
                item.TargetId,
                item.OccurredAtUtc,
                item.Summary)).ToArray(),
            response.ContinuationToken);
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
