using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class SetupLayout : IDisposable
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    private MudThemeProvider? _provider;
    private bool _dark;
    private bool _systemDark;

    protected override void OnInitialized() => ThemeService.Changed += Refresh;

    public void Dispose() => ThemeService.Changed -= Refresh;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ThemeService.LoadAsync();
            _systemDark = _provider is not null && await _provider.GetSystemDarkModeAsync();
            Refresh();
        }
    }

    private void ToggleDark() => ThemeService.SetMode(_dark ? "light" : "dark");

    private void Refresh() => InvokeAsync(() =>
    {
        _dark = ThemeService.Mode switch
        {
            "dark" => true,
            "light" => false,
            _ => _systemDark,
        };
        StateHasChanged();
    });
}
