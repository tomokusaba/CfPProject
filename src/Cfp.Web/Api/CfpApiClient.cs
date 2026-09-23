using System.Net;
using System.Net.Http.Headers;
using Cfp.Contracts.V1;
using MemoryPack;

namespace Cfp.Web.Api;

public sealed class CfpApiClient(HttpClient httpClient, IConfiguration configuration)
{
    private const string MemoryPackMediaType = "application/octet-stream";

    public async Task<PublicConferencePageDto> ListPublicConferencesAsync(
        int pageSize = 20,
        string? continuationToken = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query += $"&search={Uri.EscapeDataString(searchTerm.Trim())}";
        }

        return await GetAsync<PublicConferencePageDto>(
            $"api/v1/public/conferences{query}",
            cancellationToken);
    }

    public Task<PublicConferenceDto> GetPublicConferenceAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        GetAsync<PublicConferenceDto>(
            $"api/v1/public/conferences/{Uri.EscapeDataString(slug)}",
            cancellationToken);

    public Task<PublicProposalTypesDto> ListPublicProposalTypesAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        GetAsync<PublicProposalTypesDto>(
            $"api/v1/public/conferences/{Uri.EscapeDataString(slug)}/proposal-types",
            cancellationToken);

    public Task<PublicScheduleDto> GetPublicScheduleAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        GetAsync<PublicScheduleDto>(
            $"api/v1/public/conferences/{Uri.EscapeDataString(slug)}/schedule",
            cancellationToken);

    public async Task<PublicProposalsDto> ListPublicProposalsAsync(
        string slug,
        int pageSize = 20,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await GetAsync<PublicProposalsDto>(
            $"api/v1/public/conferences/{Uri.EscapeDataString(slug)}/proposals{query}",
            cancellationToken);
    }

    public Task<PublicProposalDto> GetPublicProposalAsync(
        string slug,
        string proposalId,
        CancellationToken cancellationToken = default) =>
        GetAsync<PublicProposalDto>(
            $"api/v1/public/conferences/{Uri.EscapeDataString(slug)}/proposals/{Uri.EscapeDataString(proposalId)}",
            cancellationToken);

    private async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
        where T : class
    {
        var baseAddress = GetBaseAddress();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MemoryPackMediaType));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response, cancellationToken);
            throw new CfpApiException(response.StatusCode, message);
        }

        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, MemoryPackMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new CfpApiException(HttpStatusCode.BadGateway, "サーバーから想定外の応答がありました。");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        try
        {
            return MemoryPackSerializer.Deserialize<T>(bytes)
                ?? throw new CfpApiException(HttpStatusCode.BadGateway, "サーバーの応答を読み取れませんでした。");
        }
        catch (MemoryPackSerializationException)
        {
            throw new CfpApiException(HttpStatusCode.BadGateway, "サーバーの応答を読み取れませんでした。");
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
            throw new InvalidOperationException(
                "API の接続先が設定されていません。wwwroot/appsettings.json の ApiBaseUrl を設定してください。");
        }

        return address.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? address
            : new Uri(address.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, MemoryPackMediaType, StringComparison.OrdinalIgnoreCase))
        {
            var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            try
            {
                var error = MemoryPackSerializer.Deserialize<ApiErrorDto>(payload);
                if (!string.IsNullOrWhiteSpace(error?.Message))
                {
                    return error.Message;
                }
            }
            catch (MemoryPackSerializationException)
            {
                return "情報の取得に失敗しました。時間をおいて再度お試しください。";
            }
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => "公開されていないか、見つからないカンファレンスです。",
            HttpStatusCode.Unauthorized => "続行するにはログインしてください。",
            HttpStatusCode.Forbidden => "この操作を行う権限がありません。",
            _ => "情報の取得に失敗しました。時間をおいて再度お試しください。"
        };
    }
}

public sealed class CfpApiException(HttpStatusCode statusCode, string userMessage)
    : Exception(userMessage)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string UserMessage { get; } = userMessage;
}
