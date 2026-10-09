using Coworkee.AuthServer.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Pages.Account;

/// <summary>
/// The app launcher: the applications users sign in to, with a home address and shown in the launcher. Clients from the configuration
/// without an own logo show the logo of the account pages.
/// </summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class AppsModel(IOpenIddictApplicationManager applications, AuthBrandingProvider branding) : PageModel
{
    public IReadOnlyList<LauncherApp> Apps { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var logo = (await branding.GetAsync(HttpContext.RequestAborted)).LogoUrl;
        var apps = new List<LauncherApp>();
        await foreach (var application in applications.ListAsync(null, null, HttpContext.RequestAborted))
        {
            var properties = await applications.GetPropertiesAsync(application, HttpContext.RequestAborted);
            var app = ClientApp.Read(properties);
            if (!app.ShowInLauncher || !ClientApp.IsWebAddress(app.ClientUri)
                || !await applications.HasPermissionAsync(application, Permissions.GrantTypes.AuthorizationCode, HttpContext.RequestAborted))
            {
                continue;
            }

            var name = await applications.GetLocalizedDisplayNameAsync(application, HttpContext.RequestAborted)
                       ?? (await applications.GetClientIdAsync(application, HttpContext.RequestAborted))!;
            var ownLogo = ClientApp.IsWebAddress(app.LogoUrl) ? app.LogoUrl : null;
            apps.Add(new LauncherApp(name, app.ClientUri!, ownLogo ?? (properties.ContainsKey(AuthClientSeeder.ManagedProperty) ? logo : null), app.Description));
        }

        Apps = [.. apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public sealed record LauncherApp(string Name, string Url, string? LogoUrl, string? Description)
    {
        public string Initial => Name[..1].ToUpperInvariant();
    }
}
