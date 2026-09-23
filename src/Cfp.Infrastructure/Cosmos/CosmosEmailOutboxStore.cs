using System.Net;
using Cfp.Application.Abstractions;
using Cfp.Domain.Notifications;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosEmailOutboxStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IEmailOutboxStore
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "conferenceData");

    public async Task<Versioned<EmailOutboxItem>?> GetAsync(
        string conferenceId,
        string outboxId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _conferenceData.ReadItemAsync<EmailOutboxDocument>(
                outboxId,
                new PartitionKey(conferenceId),
                cancellationToken: cancellationToken);
            return new Versioned<EmailOutboxItem>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Versioned<EmailOutboxItem>> SaveAsync(
        EmailOutboxItem item,
        string expectedEtag,
        CancellationToken cancellationToken)
    {
        var document = ToDocument(item);
        try
        {
            var response = await _conferenceData.ReplaceItemAsync(
                document,
                document.Id,
                new PartitionKey(item.ConferenceId),
                new ItemRequestOptions { IfMatchEtag = expectedEtag },
                cancellationToken);
            return new Versioned<EmailOutboxItem>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            throw new RequestConflictException("Email outbox state changed. Reload before retrying.");
        }
    }

    public async Task<IReadOnlyList<Versioned<EmailOutboxItem>>> ListUnresolvedAsync(
        DateTimeOffset expiredSendingBeforeUtc,
        DateTimeOffset unreportedAcceptedBeforeUtc,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.type = @type AND ((c.status = @sending AND c.leaseExpiresAtUtc <= @sendingBefore) OR (c.status = @accepted AND c.updatedAtUtc <= @acceptedBefore))")
            .WithParameter("@type", "emailOutbox")
            .WithParameter("@sending", EmailOutboxStatus.Sending.ToString())
            .WithParameter("@sendingBefore", expiredSendingBeforeUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            .WithParameter("@accepted", EmailOutboxStatus.Accepted.ToString())
            .WithParameter("@acceptedBefore", unreportedAcceptedBeforeUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        using var iterator = _conferenceData.GetItemQueryIterator<EmailOutboxDocument>(
            query,
            requestOptions: new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
        {
            return [];
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        return response.Resource
            .Select(document => new Versioned<EmailOutboxItem>(
                Map(document),
                RequireETag(document.ETag)))
            .ToArray();
    }

    public async Task<EmailOutboxPage> ListByConferenceAsync(
        string conferenceId,
        int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type ORDER BY c.createdAtUtc DESC")
            .WithParameter("@type", "emailOutbox");
        using var iterator = _conferenceData.GetItemQueryIterator<EmailOutboxDocument>(
            query,
            continuationToken,
            new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(conferenceId),
                MaxItemCount = pageSize
            });
        if (!iterator.HasMoreResults)
        {
            return new EmailOutboxPage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        return new EmailOutboxPage(
            response.Resource
                .Select(document => new Versioned<EmailOutboxItem>(
                    Map(document),
                    RequireETag(document.ETag)))
                .ToArray(),
            response.ContinuationToken);
    }

    private static EmailOutboxDocument ToDocument(EmailOutboxItem item) => new()
    {
        Id = item.Id,
        ConferenceId = item.ConferenceId,
        Type = "emailOutbox",
        RecipientUserId = item.RecipientUserId,
        Category = item.Category.ToString(),
        TemplateId = item.TemplateId,
        Status = item.Status.ToString(),
        ProviderMessageId = item.ProviderMessageId,
        AttemptId = item.AttemptId,
        LeaseExpiresAtUtc = item.LeaseExpiresAtUtc,
        StatusReason = item.StatusReason,
        Subject = item.Subject,
        PlainTextContent = item.PlainTextContent,
        SenderAddress = item.SenderAddress,
        CreatedAtUtc = item.CreatedAtUtc,
        UpdatedAtUtc = item.UpdatedAtUtc
    };

    private static EmailOutboxItem Map(EmailOutboxDocument document)
    {
        if (!Enum.TryParse<EmailCategory>(document.Category, ignoreCase: false, out var category) ||
            !Enum.TryParse<EmailOutboxStatus>(document.Status, ignoreCase: false, out var status))
        {
            throw new JsonSerializationException("Email outbox has an invalid category or status.");
        }

        return new EmailOutboxItem(
            document.Id,
            document.ConferenceId,
            document.RecipientUserId,
            category,
            document.TemplateId,
            status,
            document.CreatedAtUtc,
            document.UpdatedAtUtc == default ? document.CreatedAtUtc : document.UpdatedAtUtc,
            document.ProviderMessageId,
            document.AttemptId,
            document.LeaseExpiresAtUtc,
            document.StatusReason,
            document.Subject,
            document.PlainTextContent,
            document.SenderAddress);
    }

    private static string RequireETag(string? etag) =>
        !string.IsNullOrWhiteSpace(etag)
            ? etag
            : throw new JsonSerializationException("Email outbox query result is missing its ETag.");

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
        [JsonProperty("providerMessageId")]
        public string? ProviderMessageId { get; set; }
        [JsonProperty("attemptId")]
        public string? AttemptId { get; set; }
        [JsonProperty("leaseExpiresAtUtc")]
        public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
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
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
    }
}
