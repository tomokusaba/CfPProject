using System.Text.Json;
using Cfp.Application.Identity;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace Cfp.Functions.Security;

public sealed class EasyAuthPrincipalReader(IConfiguration configuration)
{
    private const int MaximumHeaderLength = 65_536;
    private readonly string _expectedProvider = configuration["ExternalId:ProviderName"] ?? "externalid";

    public AuthenticatedIdentity? TryReadIdentity(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var values))
        {
            return null;
        }

        var headers = values.ToArray();
        if (headers.Length != 1)
        {
            return null;
        }

        return ParseHeader(headers[0], _expectedProvider);
    }

    public static AuthenticatedIdentity? ParseHeader(string? encodedPrincipal, string expectedProvider)
    {
        if (string.IsNullOrWhiteSpace(encodedPrincipal) || encodedPrincipal.Length > MaximumHeaderLength)
        {
            return null;
        }

        try
        {
            var json = Convert.FromBase64String(encodedPrincipal);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("auth_typ", out var provider) ||
                !string.Equals(provider.GetString(), expectedProvider, StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("claims", out var claims) ||
                claims.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var issuer = FindSingleClaim(claims, "iss", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/issuer");
            var subject = FindSingleClaim(
                claims,
                "sub",
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
            var verifiedEmail = IsEmailVerified(claims)
                ? FindSingleClaim(
                    claims,
                    "email",
                    "emails",
                    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                : null;

            return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)
                ? null
                : new AuthenticatedIdentity(issuer, subject, verifiedEmail);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsEmailVerified(JsonElement claims)
    {
        var verifiedClaim = FindSingleClaim(claims, "email_verified");
        return bool.TryParse(verifiedClaim, out var verified) && verified;
    }

    private static string? FindSingleClaim(JsonElement claims, params string[] acceptedTypes)
    {
        var values = claims
            .EnumerateArray()
            .Where(claim => claim.ValueKind == JsonValueKind.Object &&
                            claim.TryGetProperty("typ", out var type) &&
                            type.ValueKind == JsonValueKind.String &&
                            acceptedTypes.Contains(type.GetString(), StringComparer.Ordinal))
            .Select(claim => claim.TryGetProperty("val", out var value) &&
                             value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        return values.Length == 1 ? values[0] : null;
    }
}
