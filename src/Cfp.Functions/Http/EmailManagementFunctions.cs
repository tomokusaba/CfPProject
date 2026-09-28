using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Abstractions;
using Cfp.Application.Authorization;
using Cfp.Application.Identity;
using Cfp.Application.Notifications;
using Cfp.Contracts.V1;
using Cfp.Domain.Conferences;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace Cfp.Functions.Http;

public sealed class EmailManagementFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ConferenceAuthorizationService authorization,
    IEmailOutboxStore outboxStore,
    PreviewTargetedEmailHandler previewHandler,
    SendTargetedEmailHandler sendHandler,
    IConfiguration configuration)
{
    [Function(nameof(GetEmailHistory))]
    public async Task<HttpResponseData> GetEmailHistory(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/mail")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var identity = principalReader.TryReadIdentity(request);
        if (identity is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Unauthorized,
                "AuthenticationRequired",
                "Sign in to view email history.",
                cancellationToken);
        }

        var actor = await actorResolution.ResolveAsync(identity, cancellationToken);
        try
        {
            await authorization.RequireRoleAsync(
                conferenceId,
                actor,
                cancellationToken,
                ConferenceRole.ConferenceOwner,
                ConferenceRole.Organizer);
        }
        catch (ConferenceAuthorizationException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                "ConferenceRoleRequired",
                "You do not have permission to view email history.",
                cancellationToken);
        }

        var query = HttpUtility.ParseQueryString(request.Url.Query);
        var pageSize = 20;
        if (query["pageSize"] is { } size &&
            (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) ||
             pageSize is < 1 or > 50))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidPageSize",
                "Page size must be between 1 and 50.",
                cancellationToken);
        }

        var continuationToken = query["continuationToken"];
        if (continuationToken is { Length: > 4096 })
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidContinuationToken",
                "The continuation token is too long.",
                cancellationToken);
        }

        var page = await outboxStore.ListByConferenceAsync(
            conferenceId,
            pageSize,
            continuationToken,
            cancellationToken);
        return await MemoryPackHttp.WriteAsync(
            request,
            HttpStatusCode.OK,
            new EmailOutboxHistoryDto
            {
                Items = page.Items.Select(item => new EmailOutboxSummaryDto
                {
                    Id = item.Value.Id,
                    RecipientUserId = item.Value.RecipientUserId,
                    Category = item.Value.Category.ToString(),
                    TemplateId = item.Value.TemplateId,
                    Status = item.Value.Status.ToString(),
                    ProviderMessageId = item.Value.ProviderMessageId,
                    CreatedAtUtc = item.Value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    StatusReason = item.Value.StatusReason
                }).ToList(),
                ContinuationToken = page.ContinuationToken
            },
            cancellationToken);
    }

    [Function(nameof(PreviewTargetedEmail))]
    public async Task<HttpResponseData> PreviewTargetedEmail(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/mail/preview")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        if (!Guid.TryParse(GetSingleHeader(request, "Idempotency-Key"), out var idempotencyKey))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "IdempotencyKeyRequired",
                "A GUID Idempotency-Key header is required.",
                cancellationToken);
        }

        PreviewTargetedEmailRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<PreviewTargetedEmailRequestDto>(request, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                cancellationToken);
        }

        if (payload.ProposalStatuses is null || payload.Subject is null || payload.PlainTextContent is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidEmailCampaign",
                "Target statuses, subject, and message are required.",
                cancellationToken);
        }

        var senderAddress = configuration["Communication:SenderAddress"];
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.ServiceUnavailable,
                "EmailSenderNotConfigured",
                "The email sender address is not configured.",
                cancellationToken);
        }

        try
        {
            var preview = await previewHandler.HandleAsync(
                conferenceId,
                new PreviewTargetedEmailCommand(
                    payload.ProposalStatuses,
                    payload.Subject,
                    payload.PlainTextContent,
                    senderAddress.Trim()),
                actor,
                idempotencyKey.ToString("D"),
                DateTimeOffset.UtcNow,
                cancellationToken);
            var response = await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new EmailCampaignPreviewDto
                {
                    CampaignId = preview.Campaign.Value.Id,
                    EligibleRecipientCount = preview.EligibleRecipientCount,
                    ExcludedRecipientCount = preview.ExcludedRecipientCount,
                    Subject = preview.Campaign.Value.Subject,
                    PlainTextContent = preview.Campaign.Value.PlainTextContent,
                    ExpiresAtUtc = preview.Campaign.Value.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    ETag = preview.Campaign.ETag,
                    ProposalStatuses = preview.Campaign.Value.TargetProposalStatuses
                        .Select(status => status.ToString())
                        .ToList(),
                    SenderAddress = preview.Campaign.Value.SenderAddress
                },
                cancellationToken);
            response.Headers.Add("ETag", preview.Campaign.ETag);
            return response;
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidEmailCampaign",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.UnprocessableEntity,
                "EmailCampaignNotReady",
                exception.Message,
                cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "EmailPreviewConflict",
                exception.Message,
                cancellationToken);
        }
    }

    [Function(nameof(SendTargetedEmail))]
    public async Task<HttpResponseData> SendTargetedEmail(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "v1/manage/conferences/{conferenceId}/mail")]
        HttpRequestData request,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        if (!MemoryPackHttp.AcceptsMemoryPack(request))
        {
            return request.CreateResponse(HttpStatusCode.NotAcceptable);
        }

        var actor = await ResolveActorAsync(request, cancellationToken);
        if (actor is null)
        {
            return await UnauthorizedAsync(request, cancellationToken);
        }

        var operationId = GetSingleHeader(request, "Idempotency-Key");
        var ifMatch = GetSingleHeader(request, "If-Match");
        if (!Guid.TryParse(operationId, out var confirmationId) || string.IsNullOrWhiteSpace(ifMatch))
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionRequired,
                "ConfirmationHeadersRequired",
                "A GUID Idempotency-Key and the preview ETag in If-Match are required.",
                cancellationToken);
        }

        SendTargetedEmailRequestDto payload;
        try
        {
            payload = await MemoryPackHttp.ReadAsync<SendTargetedEmailRequestDto>(request, cancellationToken);
        }
        catch (MemoryPackRequestException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(payload.CampaignId) || payload.Reason is null)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidEmailCampaign",
                "Campaign ID and send reason are required.",
                cancellationToken);
        }

        try
        {
            var campaign = await sendHandler.HandleAsync(
                conferenceId,
                payload.CampaignId,
                actor,
                payload.Reason,
                confirmationId.ToString("D"),
                ifMatch,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.Accepted,
                new EmailCampaignSendResultDto
                {
                    CampaignId = campaign.Value.Id,
                    State = campaign.Value.State.ToString(),
                    RecipientCount = campaign.Value.RecipientUserIds.Count
                },
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.NotFound,
                "EmailPreviewNotFound",
                "The email preview was not found.",
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return await ForbiddenAsync(request, cancellationToken);
        }
        catch (RequestConflictException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.PreconditionFailed,
                "EmailPreviewConflict",
                exception.Message,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.BadRequest,
                "InvalidEmailCampaign",
                exception.Message,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "EmailCampaignConflict",
                exception.Message,
                cancellationToken);
        }
    }

    private async Task<Actor?> ResolveActorAsync(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var identity = principalReader.TryReadIdentity(request);
        return identity is null
            ? null
            : await actorResolution.ResolveAsync(identity, cancellationToken);
    }

    private static Task<HttpResponseData> UnauthorizedAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Unauthorized,
            "AuthenticationRequired",
            "Sign in to manage email communication.",
            cancellationToken);

    private static Task<HttpResponseData> ForbiddenAsync(
        HttpRequestData request,
        CancellationToken cancellationToken) =>
        MemoryPackHttp.WriteErrorAsync(
            request,
            HttpStatusCode.Forbidden,
            "ConferenceRoleRequired",
            "You do not have permission to manage email communication.",
            cancellationToken);

    private static string? GetSingleHeader(HttpRequestData request, string name)
    {
        if (!request.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var headers = values.ToArray();
        return headers.Length == 1 ? headers[0] : null;
    }
}
