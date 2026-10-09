using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Pages.Account;

/// <summary>Asks the user before a client with explicit consent gets tokens; allowing records a permanent authorization.</summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ConsentModel(IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations, IOpenIddictScopeManager scopes) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string ApplicationName { get; private set; } = string.Empty;

    public IReadOnlyList<string> Scopes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (await RequestAsync() is not { } request)
        {
            return BadRequest();
        }

        ApplicationName = await applications.GetLocalizedDisplayNameAsync(request.Application) ?? request.ClientId;
        var names = new List<string>();
        foreach (var name in request.Scopes)
        {
            names.Add(await scopes.FindByNameAsync(name) is { } scope ? await scopes.GetLocalizedDisplayNameAsync(scope) ?? name : name);
        }

        Scopes = names;
        return Page();
    }

    public async Task<IActionResult> OnPostAllowAsync()
    {
        if (await RequestAsync() is not { } request)
        {
            return BadRequest();
        }

        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var identity = new ClaimsIdentity("Consent");
        identity.SetClaim(Claims.Subject, subject);
        await authorizations.CreateAsync(new ClaimsPrincipal(identity), subject, (await applications.GetIdAsync(request.Application))!,
            AuthorizationTypes.Permanent, [.. request.Scopes]);
        return LocalRedirect(ReturnUrl!);
    }

    public async Task<IActionResult> OnPostDenyAsync() =>
        await RequestAsync() is null ? BadRequest() : LocalRedirect(QueryHelpers.AddQueryString(ReturnUrl!, AuthEndpoints.ConsentDenied, "1"));

    private async Task<(object Application, string ClientId, string[] Scopes)?> RequestAsync()
    {
        if (ReturnUrl is null || !Url.IsLocalUrl(ReturnUrl) || !ReturnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal))
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(ReturnUrl[ReturnUrl.IndexOf('?', StringComparison.Ordinal)..]);
        var clientId = query[Parameters.ClientId].ToString();
        return clientId.Length > 0 && await applications.FindByClientIdAsync(clientId) is { } application
            ? (application, clientId, query[Parameters.Scope].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            : null;
    }

}
