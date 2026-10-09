using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeUserMenu
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private string SignInHref => Options.SignInHref(Nav.ToBaseRelativePath(Nav.Uri));

    private async Task LogoutAsync()
    {
        var logout = await Api.LogoutAsync();
        Nav.NavigateTo(logout.Redirect, forceLoad: true);
    }
}
