using System.Globalization;
using System.Net;
using System.Web;
using Cfp.Application.Authorization;
using Cfp.Application.Auditing;
using Cfp.Application.Identity;
using Cfp.Contracts.V1;
using Cfp.Functions.Security;
using Cfp.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Http;

public sealed class AuditFunctions(
    EasyAuthPrincipalReader principalReader,
    ActorResolutionService actorResolution,
    ListConferenceAuditEventsHandler listHandler)
{
    [Function(nameof(ListConferenceAuditEvents))]
    public async Task<HttpResponseData> ListConferenceAuditEvents(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "v1/manage/conferences/{conferenceId}/audit")]
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
                "Sign in to view the conference audit log.",
                cancellationToken);
        }

        var actor = await actorResolution.ResolveAsync(identity, cancellationToken);
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

        try
        {
            var page = await listHandler.HandleAsync(
                conferenceId,
                actor,
                pageSize,
                continuationToken,
                cancellationToken);
            return await MemoryPackHttp.WriteAsync(
                request,
                HttpStatusCode.OK,
                new AuditEventsDto
                {
                    Events = page.Events.Select(item => new AuditEventDto
                    {
                        Id = item.Id,
                        ActorUserId = item.ActorUserId,
                        Operation = item.Operation,
                        TargetId = item.TargetId,
                        OccurredAtUtc = item.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                        Summary = item.Summary
                    }).ToList(),
                    ContinuationToken = page.ContinuationToken
                },
                cancellationToken);
        }
        catch (ConferenceAuthorizationException)
        {
            return await MemoryPackHttp.WriteErrorAsync(
                request,
                HttpStatusCode.Forbidden,
                "ConferenceRoleRequired",
                "You do not have permission to view this audit log.",
                cancellationToken);
        }
    }
}
