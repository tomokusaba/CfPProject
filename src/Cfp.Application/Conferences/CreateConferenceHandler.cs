using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cfp.Application.Abstractions;
using Cfp.Application.Auditing;
using Cfp.Application.Identity;
using Cfp.Domain.Conferences;

namespace Cfp.Application.Conferences;

public sealed record CreateConferenceCommand(
    string Slug,
    string Title,
    string Description,
    string TimeZoneId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset CfpOpensAtUtc,
    DateTimeOffset CfpClosesAtUtc);

public sealed class CreateConferenceHandler(IConferenceManagementStore conferenceStore)
{
    public Task<Conference> HandleAsync(
        CreateConferenceCommand command,
        Actor actor,
        string idempotencyKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(idempotencyKey, out var operationGuid))
        {
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(idempotencyKey));
        }

        var canonicalCommand = JsonSerializer.Serialize(command);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalCommand)))
            .ToLowerInvariant();
        var conferenceId = "conf_" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{actor.UserId}\n{operationGuid:N}")))
            .ToLowerInvariant()[..32];

        var conference = Conference.Create(
            conferenceId,
            command.Slug,
            command.Title,
            command.Description,
            command.TimeZoneId,
            command.StartsAtUtc,
            command.EndsAtUtc,
            command.CfpOpensAtUtc,
            command.CfpClosesAtUtc);
        var membership = new ConferenceMembership(
            conferenceId,
            actor.UserId,
            ConferenceRole.ConferenceOwner,
            IsActive: true);
        var auditEvent = new AuditEvent(
            $"audit:{operationGuid:N}",
            conferenceId,
            actor.UserId,
            "ConferenceCreated",
            conferenceId,
            nowUtc.ToUniversalTime(),
            "Conference draft created.");

        return conferenceStore.CreateAsync(
            conference,
            membership,
            auditEvent,
            operationGuid.ToString("N"),
            requestHash,
            cancellationToken);
    }
}
