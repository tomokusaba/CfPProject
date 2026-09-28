using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cfp.Functions.Triggers;

public sealed class EmailOutboxChangeFeedFunction(ILogger<EmailOutboxChangeFeedFunction> logger)
{
    private static readonly JsonSerializerOptions QueueJsonOptions = new(JsonSerializerDefaults.Web);

    [Function(nameof(EmailOutboxChangeFeedFunction))]
    [QueueOutput("email-jobs", Connection = "AzureWebJobsStorage")]
    public string[] Run(
        [CosmosDBTrigger(
            databaseName: "%COSMOS_DATABASE_NAME%",
            containerName: "conferenceData",
            Connection = "CosmosConnection",
            LeaseContainerName = "functionLeases",
            CreateLeaseContainerIfNotExists = false)]
        IReadOnlyList<JsonElement>? changedDocuments)
    {
        if (changedDocuments is null || changedDocuments.Count == 0)
        {
            return [];
        }

        var jobs = new List<string>();
        foreach (var document in changedDocuments)
        {
            if (!HasString(document, "type", "emailOutbox") ||
                !HasString(document, "status", "Ready") ||
                !TryGetString(document, "conferenceId", out var conferenceId) ||
                !TryGetString(document, "id", out var outboxId))
            {
                continue;
            }

            jobs.Add(JsonSerializer.Serialize(
                new EmailQueueMessage(conferenceId, outboxId),
                QueueJsonOptions));
        }

        logger.LogInformation(
            "Queued {EmailOutboxCount} ready email outbox items.",
            jobs.Count);
        return jobs.ToArray();
    }

    private static bool HasString(JsonElement document, string propertyName, string expected) =>
        TryGetString(document, propertyName, out var value) &&
        string.Equals(value, expected, StringComparison.Ordinal);

    private static bool TryGetString(JsonElement document, string propertyName, out string value)
    {
        value = string.Empty;
        return document.ValueKind == JsonValueKind.Object &&
               document.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private sealed record EmailQueueMessage(string ConferenceId, string OutboxId);
}
