using System.Reflection;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// What AuthorizeRouteView shows instead of a page: sign in, or, while the user acts within one role, switch to another of
/// their roles that grants the page's permissions. It turns into the page by itself once the permissions arrive.
/// </summary>
public partial class NotAllowed
{
    private bool _signedIn;
    private IReadOnlyList<MyRoleDto> _granting = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    /// <summary>The page; its [Authorize(Policy = "perm:…")] attributes name the permissions to look for.</summary>
    [Parameter] public Type? PageType { get; set; }

    private string SignInHref => Options.SignInHref(Nav.ToBaseRelativePath(Nav.Uri));

    protected override async Task OnParametersSetAsync()
    {
        _signedIn = AuthenticationState is not null && (await AuthenticationState).User.Identity?.IsAuthenticated == true;
        _granting = _signedIn ? await GrantingAsync() : [];
    }

    private async Task<IReadOnlyList<MyRoleDto>> GrantingAsync()
    {
        var required = PageType?.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).OfType<string>().Where(p => p.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
            .Select(p => p[PermissionPolicy.Prefix.Length..]).ToList() ?? [];
        if (required.Count == 0)
        {
            return [];
        }

        try
        {
            IReadOnlyList<MyRoleDto>? granting = null;
            foreach (var permission in required)
            {
                var mine = await Api.GetMyRolesAsync(permission);
                if (!mine.Enabled || mine.ActiveRoleId is null)
                {
                    return [];
                }

                var grants = mine.Roles.Where(r => r.Grants && r.Id != mine.ActiveRoleId).ToList();
                granting = granting is null ? grants : [.. granting.Where(g => grants.Any(r => r.Id == g.Id))];
            }

            return granting ?? [];
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private async Task SwitchAsync(Guid roleId)
    {
        if (await Snackbar.RunAsync(() => Api.SetActiveRoleAsync(roleId)))
        {
            Permissions.Reset();
        }
    }
}
