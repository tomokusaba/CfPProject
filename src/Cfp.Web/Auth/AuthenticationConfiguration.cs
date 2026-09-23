namespace Cfp.Web.Auth;

public sealed record AuthenticationConfiguration(
    bool IsConfigured,
    string Authority,
    string ClientId,
    string ApiScope)
{
    public static AuthenticationConfiguration From(IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"] ?? string.Empty;
        var clientId = configuration["Authentication:ClientId"] ?? string.Empty;
        var apiScope = configuration["Authentication:ApiScope"] ?? string.Empty;
        var configured = Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) &&
                         authorityUri.Scheme == Uri.UriSchemeHttps &&
                         Guid.TryParse(clientId, out _) &&
                         Uri.TryCreate(apiScope, UriKind.Absolute, out _);

        return new AuthenticationConfiguration(configured, authority, clientId, apiScope);
    }
}
