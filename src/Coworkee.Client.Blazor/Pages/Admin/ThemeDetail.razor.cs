using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class ThemeDetail : IDisposable
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private ThemeDto? _theme;
    private MudTheme? _editing;
    private string _name = string.Empty;
    private string? _logoSvg;
    private string? _customCss;
    private string? _errors;
    private VersionHistory? _history;
    private AuditTimeline? _timeline;

    [Parameter] public Guid Id { get; set; }

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _theme = (await Api.GetThemesAsync()).FirstOrDefault(t => t.Id == Id);
        if (_theme is null)
        {
            return;
        }

        _editing = ThemeMapper.ToMudTheme(_theme);
        _name = _theme.Name;
        _logoSvg = _theme.LogoSvg;
        _customCss = _theme.CustomCss;
        ThemeService.Preview(_editing);
    }

    private void OnThemeChanged(MudTheme theme)
    {
        _editing = theme;
        ThemeService.Preview(theme);
    }

    private async Task SaveAsync()
    {
        _errors = null;
        try
        {
            _theme = await Api.UpdateThemeAsync(Id, ThemeMapper.ToRequest(_name, _editing!, _logoSvg, _customCss));
            Snackbar.Add("Theme saved.", Severity.Success);
            if (_history is not null)
            {
                await _history.ReloadAsync();
            }

            if (_timeline is not null)
            {
                await _timeline.ReloadAsync();
            }
        }
        catch (ApiException exception)
        {
            _errors = exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message;
        }
    }

    public void Dispose() => _ = ThemeService.LoadAsync();
}
