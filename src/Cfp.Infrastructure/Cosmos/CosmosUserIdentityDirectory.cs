using System.Net;
using System.Security.Cryptography;
using System.Text;
using Cfp.Application.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Cfp.Infrastructure.Cosmos;

public sealed class CosmosUserIdentityDirectory(
    CosmosClient cosmosClient,
    IConfiguration configuration) : IUserIdentityDirectory
{
    private readonly Container _identityDirectory =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "identityDirectory");
    private readonly Container _userProfiles =
        cosmosClient.GetContainer(configuration["Cosmos:DatabaseName"] ?? "cfp", "userProfiles");

    public async Task<string> GetOrCreateUserIdAsync(
        AuthenticatedIdentity identity,
        CancellationToken cancellationToken)
    {
        var identityKey = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{identity.Issuer}\n{identity.Subject}")))
            .ToLowerInvariant();

        IdentityMapping mapping;
        try
        {
            var response = await _identityDirectory.ReadItemAsync<IdentityMapping>(
                identityKey,
                new PartitionKey(identityKey),
                cancellationToken: cancellationToken);
            mapping = response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var candidate = new IdentityMapping
            {
                Id = identityKey,
                IdentityKey = identityKey,
                UserId = $"user_{Guid.NewGuid():N}"
            };

            try
            {
                var response = await _identityDirectory.CreateItemAsync(
                    candidate,
                    new PartitionKey(identityKey),
                    cancellationToken: cancellationToken);
                mapping = response.Resource;
            }
            catch (CosmosException createException) when (createException.StatusCode == HttpStatusCode.Conflict)
            {
                var response = await _identityDirectory.ReadItemAsync<IdentityMapping>(
                    identityKey,
                    new PartitionKey(identityKey),
                    cancellationToken: cancellationToken);
                mapping = response.Resource;
            }
        }

        await EnsureProfileAsync(mapping.UserId, identity.VerifiedEmail, cancellationToken);
        return mapping.UserId;
    }

    private async Task EnsureProfileAsync(
        string userId,
        string? verifiedEmail,
        CancellationToken cancellationToken)
    {
        var profile = new UserProfile
        {
            Id = userId,
            UserId = userId,
            Type = "speakerProfile",
            SchemaVersion = 1,
            DisplayName = string.Empty,
            Biography = string.Empty,
            Email = verifiedEmail,
            EmailVerified = !string.IsNullOrWhiteSpace(verifiedEmail),
            ConferenceOperationsOptIn = false,
            EmailSuppressed = false,
            EmailSuppressionReason = null
        };

        try
        {
            await _userProfiles.CreateItemAsync(
                profile,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existingResponse = await _userProfiles.ReadItemAsync<UserProfile>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            var existing = existingResponse.Resource;
            if (!existing.EmailVerified && !string.IsNullOrWhiteSpace(verifiedEmail))
            {
                existing.Email = verifiedEmail;
                existing.EmailVerified = true;
                await _userProfiles.ReplaceItemAsync(
                    existing,
                    userId,
                    new PartitionKey(userId),
                    new ItemRequestOptions { IfMatchEtag = existingResponse.ETag },
                    cancellationToken);
            }
        }
    }

    private sealed class IdentityMapping
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("identityKey")]
        public string IdentityKey { get; set; } = string.Empty;

        [JsonProperty("userId")]
        public string UserId { get; set; } = string.Empty;
    }

    private sealed class UserProfile
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
    }
}
