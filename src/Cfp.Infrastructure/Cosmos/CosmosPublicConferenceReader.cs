using System.Net;
using Cfp.Application.PublicConferences;
using Cfp.Domain.Conferences;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosPublicConferenceReader(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IPublicConferenceReader
{
    private readonly Container _conferenceData =
        cosmosClient.GetContainer(
            configuration["Cosmos:DatabaseName"] ?? "cfp",
            "conferenceData");

    private readonly Container _conferenceDirectory =
        cosmosClient.GetContainer(
            configuration["Cosmos:DatabaseName"] ?? "cfp",
            "conferenceDirectory");

    public async Task<PublicConferencePage> ListAsync(
        int pageSize,
        string? continuationToken,
        string searchTerm,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var queryText = "SELECT * FROM c WHERE c.state = @state";
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            queryText += " AND CONTAINS(c.title, @search, true)";
        }

        queryText += " ORDER BY c.startsAtUtc";
        var query = new QueryDefinition(queryText).WithParameter("@state", "active");
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query.WithParameter("@search", searchTerm);
        }

        using var iterator = _conferenceDirectory.GetItemQueryIterator<ConferenceDirectoryItem>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
        {
            return new PublicConferencePage([], null);
        }

        var response = await iterator.ReadNextAsync(cancellationToken);
        var items = response.Resource
            .Where(item => item.State == "active")
            .Select(item => new PublicConferenceSummary(
                item.Slug,
                item.Title,
                item.StartsAtUtc,
                item.TimeZoneId,
                GetCfpAvailability(item, nowUtc)))
            .ToArray();

        return new PublicConferencePage(items, response.ContinuationToken);
    }

    public async Task<Conference?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        ConferenceDirectoryItem directoryItem;
        try
        {
            var directoryResponse = await _conferenceDirectory.ReadItemAsync<ConferenceDirectoryItem>(
                "entry",
                new PartitionKey(slug),
                cancellationToken: cancellationToken);
            directoryItem = directoryResponse.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (directoryItem.State != "active")
        {
            return null;
        }

        ConferenceDataItem conferenceItem;
        try
        {
            var conferenceResponse = await _conferenceData.ReadItemAsync<ConferenceDataItem>(
                "conference",
                new PartitionKey(directoryItem.ConferenceId),
                cancellationToken: cancellationToken);
            conferenceItem = conferenceResponse.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var conference = Conference.Create(
            conferenceItem.ConferenceId,
            conferenceItem.Slug,
            conferenceItem.Title,
            conferenceItem.Description ?? string.Empty,
            conferenceItem.TimeZoneId,
            conferenceItem.StartsAtUtc,
            conferenceItem.EndsAtUtc,
            conferenceItem.CfpOpensAtUtc,
            conferenceItem.CfpClosesAtUtc) with
        {
            LifecycleState = ParseEnum<ConferenceLifecycleState>(conferenceItem.LifecycleState),
            Visibility = ParseEnum<ConferenceVisibility>(conferenceItem.Visibility),
            CfpState = ParseEnum<CfpPublicationState>(conferenceItem.CfpState),
            PublicShowcaseEnabled = conferenceItem.PublicShowcaseEnabled
        };

        return conference.LifecycleState == ConferenceLifecycleState.Active &&
               conference.Visibility == ConferenceVisibility.Public
            ? conference
            : null;
    }

    private static CfpAvailability GetCfpAvailability(
        ConferenceDirectoryItem item,
        DateTimeOffset nowUtc)
    {
        if (item.CfpState == CfpPublicationState.Draft.ToString())
        {
            return CfpAvailability.Unpublished;
        }

        if (item.CfpState == CfpPublicationState.ManuallyClosed.ToString() ||
            nowUtc >= item.CfpClosesAtUtc)
        {
            return CfpAvailability.Closed;
        }

        return nowUtc < item.CfpOpensAtUtc
            ? CfpAvailability.Scheduled
            : CfpAvailability.Open;
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new JsonSerializationException($"Invalid {typeof(TEnum).Name} value in conference data.");

    private sealed class ConferenceDirectoryItem
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
    }

    private sealed class ConferenceDataItem
    {
        [JsonProperty("conferenceId")]
        public string ConferenceId { get; set; } = string.Empty;

        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string? Description { get; set; }

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
    }
}
