using System.Text.Json;
using Cfp.Application.Notifications;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Azure.Messaging.EventGrid;

namespace Cfp.Functions.Triggers;

public sealed class EmailDeliveryReportFunction(
    EmailDeliveryReportHandler handler,
    IConfiguration configuration)
{
    [Function(nameof(EmailDeliveryReportFunction))]
    public async Task Run(
        [EventGridTrigger]
        EventGridEvent eventGridEvent,
        CancellationToken cancellationToken)
    {
        var expectedTopic = configuration["Communication:ResourceId"];
        if (string.IsNullOrWhiteSpace(expectedTopic) ||
            !string.Equals(eventGridEvent.Topic, expectedTopic, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Event Grid email report came from an unrecognized ACS resource.");
        }

        if (!string.Equals(
                eventGridEvent.EventType,
                "Microsoft.Communication.EmailDeliveryReportReceived",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Event Grid event type is not an ACS email delivery report.");
        }

        using var data = JsonDocument.Parse(eventGridEvent.Data.ToStream(), new JsonDocumentOptions { MaxDepth = 16 });
        if (data.RootElement.ValueKind != JsonValueKind.Object ||
            !TryGetString(data.RootElement, "messageId", out var messageId) ||
            !TryGetString(data.RootElement, "status", out var status))
        {
            throw new JsonException("ACS email delivery report is missing a message ID or status.");
        }

        await handler.HandleAsync(
            eventGridEvent.Id,
            messageId,
            status,
            cancellationToken);
    }

    private static bool TryGetString(JsonElement data, string name, out string value)
    {
        value = string.Empty;
        return data.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}
