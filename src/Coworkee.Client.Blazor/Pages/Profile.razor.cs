using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Pages;

public partial class Profile
{
    private static readonly string[] BuiltIn = [string.Empty, "security", "notifications", "settings"];
    private List<ProfileTab> _extra = [];
    private bool _ready;
    private bool _activated;
    private MudBlazor.MudTabs? _tabs;

    [Parameter] public string? Tab { get; set; }

    [Inject] private IEnumerable<ProfileTab> Tabs { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private IEnumerable<string> Keys => BuiltIn.Concat(_extra.Select(t => t.Key));

    private int ActiveIndex => Math.Max(0, Keys.ToList().FindIndex(k => string.Equals(k, Tab ?? string.Empty, StringComparison.OrdinalIgnoreCase)));

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthenticationState).User;
        foreach (var tab in Tabs.OrderBy(t => t.Order))
        {
            if (tab.Permission is null || (await Authorization.AuthorizeAsync(user, PermissionPolicy.For(tab.Permission))).Succeeded)
            {
                _extra.Add(tab);
            }
        }

        _ready = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_tabs is not null && !_activated)
        {
            _activated = true;
            await _tabs.ActivatePanelAsync(ActiveIndex);
        }
    }

    private void Select(int index)
    {
        var key = Keys.ElementAt(index);
        Tab = key;
        Nav.NavigateTo(key.Length == 0 ? "/profile" : $"/profile/{key}", replace: true);
    }
}
