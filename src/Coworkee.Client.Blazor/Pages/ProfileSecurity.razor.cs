using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Pages;

public partial class ProfileSecurity
{
    private string? _manage;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected override async Task OnInitializedAsync() =>
        _manage = (await AuthenticationState).User.FindFirst("manage_url")?.Value?.TrimEnd('/');
}
