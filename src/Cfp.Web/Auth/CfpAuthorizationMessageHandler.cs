using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Cfp.Web.Auth;

public sealed class CfpAuthorizationMessageHandler(
    IAccessTokenProvider accessTokenProvider,
    NavigationManager navigation,
    IConfiguration configuration,
    AuthenticationConfiguration authenticationConfiguration)
    : AuthorizationMessageHandler(accessTokenProvider, navigation)
{
    public CfpAuthorizationMessageHandler Configure()
    {
        var apiBaseUrl = configuration["ApiBaseUrl"];
        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri) ||
            (apiUri.Scheme != Uri.UriSchemeHttps &&
             !(apiUri.Scheme == Uri.UriSchemeHttp && apiUri.IsLoopback)) ||
            !string.IsNullOrEmpty(apiUri.UserInfo) ||
            !string.IsNullOrEmpty(apiUri.Query) ||
            !string.IsNullOrEmpty(apiUri.Fragment))
        {
            throw new InvalidOperationException("A valid HTTPS ApiBaseUrl is required for authenticated API calls.");
        }

        ConfigureHandler(
            authorizedUrls: [apiUri.AbsoluteUri],
            scopes: [authenticationConfiguration.ApiScope]);
        return this;
    }
}
