using System.Net;
using Cfp.Application.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosUserProfileStore(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IUserProfileStore
{
    private readonly Container _userProfiles =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "userProfiles");

    public async Task<Versioned<SpeakerProfile>?> GetAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _userProfiles.ReadItemAsync<UserProfileDocument>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            return new Versioned<SpeakerProfile>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Versioned<SpeakerProfile>> UpdateAsync(
        SpeakerProfile profile,
        string expectedEtag,
        CancellationToken cancellationToken)
    {
        var current = await _userProfiles.ReadItemAsync<UserProfileDocument>(
            profile.UserId,
            new PartitionKey(profile.UserId),
            cancellationToken: cancellationToken);

        if (!string.Equals(current.ETag, expectedEtag, StringComparison.Ordinal))
        {
            throw new RequestConflictException("The profile changed. Reload it before saving.");
        }

        var document = current.Resource;
        document.DisplayName = profile.DisplayName;
        document.Biography = profile.Biography;
        document.ConferenceOperationsOptIn = profile.ConferenceOperationsOptIn;
        try
        {
            var response = await _userProfiles.ReplaceItemAsync(
                document,
                document.Id,
                new PartitionKey(profile.UserId),
                new ItemRequestOptions { IfMatchEtag = expectedEtag },
                cancellationToken);
            return new Versioned<SpeakerProfile>(Map(response.Resource), response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new RequestConflictException("The profile changed. Reload it before saving.");
        }
    }

    public async Task SetEmailSuppressedAsync(
        string userId,
        bool suppressed,
        string reason,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = await _userProfiles.ReadItemAsync<UserProfileDocument>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            if (current.Resource.EmailSuppressed == suppressed &&
                (!suppressed || current.Resource.EmailSuppressionReason == reason))
            {
                return;
            }

            current.Resource.EmailSuppressed = suppressed;
            current.Resource.EmailSuppressionReason = suppressed ? reason : null;
            try
            {
                await _userProfiles.ReplaceItemAsync(
                    current.Resource,
                    userId,
                    new PartitionKey(userId),
                    new ItemRequestOptions { IfMatchEtag = current.ETag },
                    cancellationToken);
                return;
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                if (attempt == 2)
                {
                    throw new RequestConflictException("The user profile changed while email suppression was updated.");
                }
            }
        }
    }

    private static SpeakerProfile Map(UserProfileDocument document) => new(
        document.UserId,
        document.DisplayName,
        document.Biography,
        document.Email,
        document.EmailVerified,
        document.ConferenceOperationsOptIn,
        document.EmailSuppressed,
        document.EmailSuppressionReason);

    private sealed class UserProfileDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }
        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;
        [JsonProperty("biography")]
        public string Biography { get; set; } = string.Empty;
        [JsonProperty("email")]
        public string? Email { get; set; }
        [JsonProperty("emailVerified")]
        public bool EmailVerified { get; set; }
        [JsonProperty("conferenceOperationsOptIn")]
        public bool ConferenceOperationsOptIn { get; set; }
        [JsonProperty("emailSuppressed")]
        public bool EmailSuppressed { get; set; }
        [JsonProperty("emailSuppressionReason")]
        public string? EmailSuppressionReason { get; set; }
        [JsonProperty("_etag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ETag { get; set; }
    }
}
