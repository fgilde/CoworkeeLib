using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeUserMenu : IDisposable
{
    [Inject] private Layout.PwaService Pwa { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    protected override void OnInitialized() => Pwa.Changed += Refresh;

    public void Dispose() => Pwa.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    private string SignInHref => Options.SignInHref(Nav.ToBaseRelativePath(Nav.Uri));

    // the BFF has the auth server switch back and returns through its login
    private void StopImpersonation() => Nav.NavigateTo("bff/stop-impersonation?returnUrl=%2F", forceLoad: true);

    private async Task LogoutAsync()
    {
        var logout = await Api.LogoutAsync();
        Nav.NavigateTo(logout.Redirect, forceLoad: true);
    }
}
