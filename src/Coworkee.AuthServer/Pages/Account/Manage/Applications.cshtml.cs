using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Pages.Account.Manage;

/// <summary>The applications with access to the account: consents and sign-in sessions, which the user can revoke.</summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ApplicationsModel(
    IOpenIddictAuthorizationManager authorizations, IOpenIddictApplicationManager applications, IOpenIddictTokenManager tokens) : PageModel
{
    public IReadOnlyList<AuthorizedApplication> Applications { get; private set; } = [];

    public bool Revoked { get; private set; }

    private string Subject => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task OnGetAsync() => Applications = await LoadAsync();

    public async Task<IActionResult> OnPostAsync(string applicationId)
    {
        if (!(await LoadAsync()).Any(a => a.Id == applicationId))
        {
            return NotFound();
        }

        await authorizations.RevokeAsync(Subject, applicationId, null, null, HttpContext.RequestAborted);
        await tokens.RevokeAsync(Subject, applicationId, null, null, HttpContext.RequestAborted);
        Revoked = true;
        Applications = await LoadAsync();
        return Page();
    }

    private async Task<IReadOnlyList<AuthorizedApplication>> LoadAsync()
    {
        var found = new Dictionary<string, AuthorizedApplication>(StringComparer.Ordinal);
        await foreach (var authorization in authorizations.FindBySubjectAsync(Subject, HttpContext.RequestAborted))
        {
            if (!await authorizations.HasStatusAsync(authorization, Statuses.Valid) || await authorizations.GetApplicationIdAsync(authorization) is not { } id)
            {
                continue;
            }

            var since = await authorizations.GetCreationDateAsync(authorization);
            var consented = await authorizations.HasTypeAsync(authorization, AuthorizationTypes.Permanent);
            var scopes = await authorizations.GetScopesAsync(authorization);
            found[id] = found.TryGetValue(id, out var known)
                ? known with
                {
                    Since = known.Since < since ? known.Since : since,
                    Consented = known.Consented || consented,
                    Scopes = [.. known.Scopes.Union(scopes)],
                }
                : new AuthorizedApplication(id, await NameAsync(id), since, consented, [.. scopes]);
        }

        return [.. found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private async Task<string> NameAsync(string id) =>
        await applications.FindByIdAsync(id) is { } application
            ? await applications.GetLocalizedDisplayNameAsync(application) ?? await applications.GetClientIdAsync(application) ?? id
            : id;

    public sealed record AuthorizedApplication(string Id, string Name, DateTimeOffset? Since, bool Consented, IReadOnlyList<string> Scopes);
}
