using System.Net;
using System.Net.Http.Headers;
using Cfp.Contracts.V1;
using Cfp.Web.Auth;
using MemoryPack;

namespace Cfp.Web.Api;

public sealed class CfpProtectedApiClient(
    HttpClient httpClient,
    IConfiguration configuration,
    AuthenticationConfiguration authenticationConfiguration)
{
    public async Task<CreatedConferenceDto> CreateConferenceAsync(
        CreateConferenceRequestDto requestDto,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!authenticationConfiguration.IsConfigured)
        {
            throw new InvalidOperationException(
                "Entra External ID is not configured. Set Authentication:Authority, ClientId, and ApiScope.");
        }

        var baseAddress = GetBaseAddress();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseAddress, "api/v1/manage/conferences"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString("D"));
        request.Content = new ByteArrayContent(MemoryPackSerializer.Serialize(requestDto));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await SendAsync<CreatedConferenceDto>(request, HttpStatusCode.Created, cancellationToken);
    }

    public async Task<ManagedProposalTypesDto> GetManagedProposalTypesAsync(
        string conferenceId,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(GetBaseAddress(), $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/proposal-types"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return await SendAsync<ManagedProposalTypesDto>(request, HttpStatusCode.OK, cancellationToken);
    }

    public async Task<ManagedConferenceDto> GetManagedConferenceAsync(
        string conferenceId,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(GetBaseAddress(), $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return await SendAsync<ManagedConferenceDto>(request, HttpStatusCode.OK, cancellationToken);
    }

    public async Task<ManagedConferenceDto> PublishConferenceAsync(
        string conferenceId,
        string ifMatch,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(GetBaseAddress(), $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/publish"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await SendAsync<ManagedConferenceDto>(request, HttpStatusCode.OK, cancellationToken);
    }

    public Task<ManagedConferenceDto> UpdateConferenceAsync(
        string conferenceId,
        UpdateConferenceRequestDto requestDto,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedConferenceDto>(
            CreateRequest(
                HttpMethod.Patch,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}",
                requestDto,
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ManagedConferenceDto> ArchiveConferenceAsync(
        string conferenceId,
        string reason,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedConferenceDto>(
            CreateRequest(
                HttpMethod.Post,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/archive",
                new ArchiveConferenceRequestDto { Reason = reason },
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public async Task<ManagedConferenceDto> SetCfpStateAsync(
        string conferenceId,
        string state,
        string reason,
        string ifMatch,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri(GetBaseAddress(), $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/cfp"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        request.Content = new ByteArrayContent(MemoryPackSerializer.Serialize(
            new SetCfpPublicationStateRequestDto { State = state, Reason = reason }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await SendAsync<ManagedConferenceDto>(request, HttpStatusCode.OK, cancellationToken);
    }

    public Task<ManagedConferenceDto> SetPublicShowcaseAsync(
        string conferenceId,
        bool enabled,
        string reason,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedConferenceDto>(
            CreateRequest(
                HttpMethod.Put,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/showcase",
                new SetPublicShowcaseRequestDto { Enabled = enabled, Reason = reason },
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public async Task<SavedProposalTypeDto> SaveProposalTypeAsync(
        string conferenceId,
        SaveProposalTypeRequestDto requestDto,
        string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(GetBaseAddress(), $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/proposal-types"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        request.Content = new ByteArrayContent(MemoryPackSerializer.Serialize(requestDto));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await SendAsync<SavedProposalTypeDto>(
            request,
            string.IsNullOrWhiteSpace(requestDto.ProposalTypeId) ? HttpStatusCode.Created : HttpStatusCode.OK,
            cancellationToken);
    }

    public async Task<MyProposalsDto> ListMyProposalsAsync(
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await SendAsync<MyProposalsDto>(
            CreateRequest<object>(HttpMethod.Get, $"api/v1/me/proposals{query}"),
            HttpStatusCode.OK,
            cancellationToken);
    }

    public async Task<ManagedConferencesDto> ListManagedConferencesAsync(
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await SendAsync<ManagedConferencesDto>(
            CreateRequest<object>(HttpMethod.Get, $"api/v1/me/conferences{query}"),
            HttpStatusCode.OK,
            cancellationToken);
    }

    public Task<ProposalDto> GetMyProposalAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProposalDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}"),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ProposalDto> CreateProposalDraftAsync(
        string conferenceId,
        ProposalDraftRequestDto requestDto,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProposalDto>(
            CreateRequest(
                HttpMethod.Post,
                $"api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals",
                requestDto,
                idempotencyKey: idempotencyKey),
            HttpStatusCode.Created,
            cancellationToken);

    public Task<ProposalDto> UpdateMyProposalAsync(
        string conferenceId,
        string proposalId,
        UpdateProposalRequestDto requestDto,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProposalDto>(
            CreateRequest(
                HttpMethod.Put,
                $"api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}",
                requestDto,
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ProposalDto> SubmitMyProposalAsync(
        string conferenceId,
        string proposalId,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProposalDto>(
            CreateRequest<object>(
                HttpMethod.Post,
                $"api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}/submit",
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ProposalDto> WithdrawMyProposalAsync(
        string conferenceId,
        string proposalId,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProposalDto>(
            CreateRequest<object>(
                HttpMethod.Post,
                $"api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}/withdraw",
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ManagedScheduleDto> GetManagedScheduleAsync(
        string conferenceId,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedScheduleDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/schedule"),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ConferenceMembersDto> GetConferenceMembersAsync(
        string conferenceId,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConferenceMembersDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/members"),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ConferenceMemberUpdatedDto> SetConferenceMemberRoleAsync(
        string conferenceId,
        string userId,
        string role,
        string reason,
        string conferenceEtag,
        CancellationToken cancellationToken = default) =>
        SendAsync<ConferenceMemberUpdatedDto>(
            CreateRequest(
                HttpMethod.Put,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/members/{Uri.EscapeDataString(userId)}",
                new SetConferenceMemberRequestDto { Role = role, Reason = reason },
                ifMatch: conferenceEtag),
            HttpStatusCode.OK,
            cancellationToken);

    public async Task<EmailOutboxHistoryDto> GetEmailHistoryAsync(
        string conferenceId,
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await SendAsync<EmailOutboxHistoryDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/mail{query}"),
            HttpStatusCode.OK,
            cancellationToken);
    }

    public Task<EmailCampaignPreviewDto> PreviewTargetedEmailAsync(
        string conferenceId,
        PreviewTargetedEmailRequestDto requestDto,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendAsync<EmailCampaignPreviewDto>(
            CreateRequest(
                HttpMethod.Post,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/mail/preview",
                requestDto,
                idempotencyKey: idempotencyKey),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<EmailCampaignSendResultDto> SendTargetedEmailAsync(
        string conferenceId,
        SendTargetedEmailRequestDto requestDto,
        string previewEtag,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendAsync<EmailCampaignSendResultDto>(
            CreateRequest(
                HttpMethod.Post,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/mail",
                requestDto,
                ifMatch: previewEtag,
                idempotencyKey: idempotencyKey),
            HttpStatusCode.Accepted,
            cancellationToken);

    public async Task<AuditEventsDto> GetAuditEventsAsync(
        string conferenceId,
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await SendAsync<AuditEventsDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/audit{query}"),
            HttpStatusCode.OK,
            cancellationToken);
    }

    public Task<ManagedScheduleDto> SaveScheduleDraftAsync(
        string conferenceId,
        SaveScheduleDraftRequestDto requestDto,
        string? ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedScheduleDto>(
            CreateRequest(
                HttpMethod.Put,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/schedule",
                requestDto,
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ManagedScheduleDto> PublishScheduleAsync(
        string conferenceId,
        string draftEtag,
        string? publicationEtag,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest<object>(
            HttpMethod.Post,
            $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/schedule/publish",
            ifMatch: draftEtag);
        if (!string.IsNullOrWhiteSpace(publicationEtag))
        {
            request.Headers.TryAddWithoutValidation("If-Match-Publication", publicationEtag);
        }

        return SendAsync<ManagedScheduleDto>(request, HttpStatusCode.OK, cancellationToken);
    }

    public async Task<ManagedProposalsDto> ListConferenceProposalsAsync(
        string conferenceId,
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await SendAsync<ManagedProposalsDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/proposals{query}"),
            HttpStatusCode.OK,
            cancellationToken);
    }

    public Task<ManagedProposalDetailsDto> GetConferenceProposalAsync(
        string conferenceId,
        string proposalId,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedProposalDetailsDto>(
            CreateRequest<object>(
                HttpMethod.Get,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}"),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ReviewSubmissionResponseDto> AssignReviewerAsync(
        string conferenceId,
        AssignReviewerRequestDto requestDto,
        CancellationToken cancellationToken = default) =>
        SendAsync<ReviewSubmissionResponseDto>(
            CreateRequest(
                HttpMethod.Post,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/reviews/assignments",
                requestDto),
            HttpStatusCode.Created,
            cancellationToken);

    public Task<ProposalDecisionResponseDto> DecideProposalAsync(
        string conferenceId,
        string proposalId,
        ProposalDecisionRequestDto requestDto,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProposalDecisionResponseDto>(
            CreateRequest(
                HttpMethod.Post,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}/decision",
                requestDto,
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ManagedProposalDto> SetProposalPublicationStateAsync(
        string conferenceId,
        string proposalId,
        bool published,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ManagedProposalDto>(
            CreateRequest(
                HttpMethod.Put,
                $"api/v1/manage/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}/publication",
                new SetProposalPublicationRequestDto { Published = published },
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<ProposalDto> SetProposalPublicationConsentAsync(
        string conferenceId,
        string proposalId,
        bool confirmed,
        string ifMatch,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(
            confirmed ? HttpMethod.Put : HttpMethod.Delete,
            $"api/v1/me/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}/public-consent",
            confirmed ? new SetProposalPublicationConsentRequestDto { Confirmed = true } : null,
            ifMatch: ifMatch);
        return SendAsync<ProposalDto>(request, HttpStatusCode.OK, cancellationToken);
    }

    public Task<SpeakerProfileDto> GetSpeakerProfileAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<SpeakerProfileDto>(
            CreateRequest<object>(HttpMethod.Get, "api/v1/me/profile"),
            HttpStatusCode.OK,
            cancellationToken);

    public Task<SpeakerProfileDto> UpdateSpeakerProfileAsync(
        UpdateSpeakerProfileRequestDto requestDto,
        string ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<SpeakerProfileDto>(
            CreateRequest(
                HttpMethod.Put,
                "api/v1/me/profile",
                requestDto,
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    public async Task<ReviewTasksDto> ListMyReviewAssignmentsAsync(
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await SendAsync<ReviewTasksDto>(
            CreateRequest<object>(HttpMethod.Get, $"api/v1/me/review-assignments{query}"),
            HttpStatusCode.OK,
            cancellationToken);
    }

    public Task<ReviewSubmissionResponseDto> SubmitReviewAsync(
        string conferenceId,
        string proposalId,
        ReviewSubmissionRequestDto requestDto,
        string? ifMatch,
        CancellationToken cancellationToken = default) =>
        SendAsync<ReviewSubmissionResponseDto>(
            CreateRequest(
                HttpMethod.Put,
                $"api/v1/reviewer/conferences/{Uri.EscapeDataString(conferenceId)}/proposals/{Uri.EscapeDataString(proposalId)}/review",
                requestDto,
                ifMatch: ifMatch),
            HttpStatusCode.OK,
            cancellationToken);

    private HttpRequestMessage CreateRequest<TRequest>(
        HttpMethod method,
        string relativePath,
        TRequest? body = null,
        string? ifMatch = null,
        Guid? idempotencyKey = null)
        where TRequest : class
    {
        var request = new HttpRequestMessage(method, new Uri(GetBaseAddress(), relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey.Value.ToString("D"));
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(MemoryPackSerializer.Serialize(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        return request;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpRequestMessage request,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        if (!authenticationConfiguration.IsConfigured)
        {
            request.Dispose();
            throw new InvalidOperationException(
                "Entra External ID is not configured. Set Authentication:Authority, ClientId, and ApiScope.");
        }

        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CfpApiException(
                    response.StatusCode,
                    await ReadErrorMessageAsync(response, cancellationToken));
            }

            if (response.StatusCode != expectedStatus ||
                !string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/octet-stream",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CfpApiException(HttpStatusCode.BadGateway, "サーバーから想定外の応答がありました。");
            }

            try
            {
                var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return MemoryPackSerializer.Deserialize<TResponse>(payload)
                    ?? throw new CfpApiException(HttpStatusCode.BadGateway, "サーバーの応答を読み取れませんでした。");
            }
            catch (MemoryPackSerializationException)
            {
                throw new CfpApiException(HttpStatusCode.BadGateway, "サーバーの応答を読み取れませんでした。");
            }
        }
    }

    private Uri GetBaseAddress()
    {
        var configuredAddress = configuration["ApiBaseUrl"];
        if (!Uri.TryCreate(configuredAddress, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttps &&
             !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)) ||
            !string.IsNullOrEmpty(address.UserInfo) ||
            !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment))
        {
            throw new InvalidOperationException("A valid HTTPS ApiBaseUrl is required for authenticated API calls.");
        }

        return address.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? address
            : new Uri(address.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            try
            {
                var error = MemoryPackSerializer.Deserialize<ApiErrorDto>(body);
                if (!string.IsNullOrWhiteSpace(error?.Message))
                {
                    return error.Message;
                }
            }
            catch (MemoryPackSerializationException)
            {
                return "処理に失敗しました。時間をおいて再度お試しください。";
            }
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "ログイン状態を確認してください。",
            HttpStatusCode.Forbidden => "この操作を行う権限がありません。",
            HttpStatusCode.Conflict => "データが更新されています。最新状態を読み直してください。",
            _ => "処理に失敗しました。時間をおいて再度お試しください。"
        };
    }
}
