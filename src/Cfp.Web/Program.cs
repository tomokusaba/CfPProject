using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Cfp.Web.Auth;
using Cfp.Web.Api;
using Cfp.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<CfpApiClient>();

var authentication = AuthenticationConfiguration.From(builder.Configuration);
builder.Services.AddSingleton(authentication);
if (authentication.IsConfigured)
{
    builder.Services.AddMsalAuthentication(options =>
    {
        builder.Configuration.Bind("Authentication", options.ProviderOptions.Authentication);
        options.ProviderOptions.DefaultAccessTokenScopes.Add(authentication.ApiScope);
    });

    builder.Services.AddTransient<CfpAuthorizationMessageHandler>(serviceProvider =>
        new CfpAuthorizationMessageHandler(
            serviceProvider.GetRequiredService<IAccessTokenProvider>(),
            serviceProvider.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            builder.Configuration,
            authentication).Configure());
}
else
{
    builder.Services.AddScoped<AuthenticationStateProvider, AnonymousAuthenticationStateProvider>();
}

var protectedApiClientBuilder = builder.Services.AddHttpClient("CfpProtectedApi");
if (authentication.IsConfigured)
{
    protectedApiClientBuilder.AddHttpMessageHandler<CfpAuthorizationMessageHandler>();
}

builder.Services.AddScoped(sp =>
    new CfpProtectedApiClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("CfpProtectedApi"),
        builder.Configuration,
        authentication));

await builder.Build().RunAsync();
