using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Cfp.Contracts.V1;
using MemoryPack;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cfp.Functions.Serialization;

public static class MemoryPackHttp
{
    private const string MediaType = "application/octet-stream";
    private const int MaximumRequestBytes = 512 * 1024;

    public static bool AcceptsMemoryPack(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("Accept", out var values))
        {
            return true;
        }

        var accepted = values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => MediaTypeWithQualityHeaderValue.TryParse(value, out var parsed) ? parsed : null)
            .Where(value => value is not null)
            .ToArray();
        var exactMatch = accepted
            .Where(value => string.Equals(value!.MediaType, MediaType, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exactMatch.Length > 0)
        {
            return exactMatch.Max(value => value!.Quality ?? 1) > 0;
        }

        return accepted.Any(value =>
            (string.Equals(value!.MediaType, "*/*", StringComparison.Ordinal) ||
             string.Equals(value.MediaType, "application/*", StringComparison.Ordinal)) &&
            (value.Quality ?? 1) > 0);
    }

    public static async Task<T> ReadAsync<T>(
        HttpRequestData request,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!request.Headers.TryGetValues("Content-Type", out var contentTypes) ||
            contentTypes.Select(value => MediaTypeHeaderValue.TryParse(value, out var parsed) ? parsed : null)
                .All(value => !string.Equals(value?.MediaType, MediaType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MemoryPackRequestException(
                HttpStatusCode.UnsupportedMediaType,
                "UnsupportedContentType",
                "Request content must use application/octet-stream.");
        }

        if (request.Headers.TryGetValues("Content-Length", out var lengths))
        {
            var values = lengths.ToArray();
            if (values.Length != 1 ||
                !long.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength) ||
                contentLength < 0)
            {
                throw new MemoryPackRequestException(
                    HttpStatusCode.BadRequest,
                    "InvalidContentLength",
                    "Request content length is invalid.");
            }

            if (contentLength > MaximumRequestBytes)
            {
                throw new MemoryPackRequestException(
                    HttpStatusCode.RequestEntityTooLarge,
                    "PayloadTooLarge",
                    "Request payload exceeds the maximum size.");
            }
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var count = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (count == 0)
            {
                break;
            }

            if (buffer.Length + count > MaximumRequestBytes)
            {
                throw new MemoryPackRequestException(
                    HttpStatusCode.RequestEntityTooLarge,
                    "PayloadTooLarge",
                    "Request payload exceeds the maximum size.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw new MemoryPackRequestException(
                HttpStatusCode.BadRequest,
                "InvalidPayload",
                "A request payload is required.");
        }

        try
        {
            return MemoryPackSerializer.Deserialize<T>(buffer.ToArray())
                ?? throw new MemoryPackRequestException(
                    HttpStatusCode.BadRequest,
                    "InvalidPayload",
                    "The request payload could not be decoded.");
        }
        catch (MemoryPackSerializationException)
        {
            throw new MemoryPackRequestException(
                HttpStatusCode.BadRequest,
                "InvalidPayload",
                "The request payload could not be decoded.");
        }
    }

    public static async Task<HttpResponseData> WriteAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", MediaType);
        await response.Body.WriteAsync(
            MemoryPackSerializer.Serialize(value),
            cancellationToken);
        return response;
    }

    public static async Task<HttpResponseData> WriteErrorAsync(
        HttpRequestData request,
        HttpStatusCode statusCode,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", MediaType);
        await response.Body.WriteAsync(
            MemoryPackSerializer.Serialize(new ApiErrorDto
            {
                Code = code,
                Message = message,
                CorrelationId = request.FunctionContext.InvocationId
            }),
            cancellationToken);
        return response;
    }
}

public sealed class MemoryPackRequestException(
    HttpStatusCode statusCode,
    string code,
    string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
