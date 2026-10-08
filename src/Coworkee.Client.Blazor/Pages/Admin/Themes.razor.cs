using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Themes : IDisposable
{
    private List<ThemePreset<CoworkeeTheme>>? _presets;
    private Dictionary<Guid, ThemeDto> _themes = [];
    private CoworkeeTheme? _theme;
    private ThemeDto? _selected;
    private int _version;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override Task OnInitializedAsync() => ReloadAsync(ThemeService.Current?.Id);

    // leaving the page drops an unsaved preview
    public void Dispose() => _ = ThemeService.LoadAsync();

    private async Task ReloadAsync(Guid? select)
    {
        var themes = await Api.GetThemesAsync();
        _themes = themes.ToDictionary(t => t.Id);
        _presets = [.. themes.Select(t => new ThemePreset<CoworkeeTheme>(t.Name, ThemeMapper.ToTheme(t)) { Id = t.Id })];
        var preset = _presets.FirstOrDefault(p => Equals(p.Id, select)) ?? _presets.FirstOrDefault(p => _themes[(Guid)p.Id].IsDefault) ?? _presets.FirstOrDefault();
        _theme = preset?.Theme;
        _selected = preset is null ? null : _themes[(Guid)preset.Id];
        _version++;
    }

    private bool CanDelete(ThemePreset<CoworkeeTheme> preset) => preset.Id is Guid id && _themes.TryGetValue(id, out var theme) && !theme.IsGlobal;

    private void Preview(CoworkeeTheme theme)
    {
        var preset = _presets?.FirstOrDefault(p => ReferenceEquals(p.Theme, theme));
        if (preset?.Id is Guid id && _themes.TryGetValue(id, out var selected))
        {
            _selected = selected;
        }

        ThemeService.Preview(theme);
    }

    private Task CreateAsync(ThemePreset<CoworkeeTheme> preset) => RunAsync(async () =>
    {
        var created = await Api.CreateThemeAsync(ThemeMapper.ToRequest(preset.Name, preset.Theme));
        await ReloadAsync(created.Id);
        Snackbar.Add(L["Theme {0} created", created.Name], Severity.Success);
    });

    private Task DeleteAsync(ThemePreset<CoworkeeTheme> preset) => RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync(L["Delete theme"], L["Delete the theme {0}? Users who chose it get the default theme.", preset.Name], L["Delete"], L["Cancel"],
                Icons.Material.Outlined.Delete))
        {
            return;
        }

        await Api.DeleteThemeAsync((Guid)preset.Id);
        await ReloadAsync(null);
    });

    private Task SaveAsync(ThemeChangedArgs<CoworkeeTheme> args) => RunAsync(async () =>
    {
        var preset = args.Preset ?? _presets?.FirstOrDefault(p => ReferenceEquals(p.Theme, args.Theme));
        if (preset?.Id is not Guid id || !_themes.TryGetValue(id, out var stored))
        {
            return;
        }

        var saved = stored.IsGlobal
            ? await Api.CreateThemeAsync(ThemeMapper.ToRequest(L["{0} copy", stored.Name], args.Theme))
            : await Api.UpdateThemeAsync(id, ThemeMapper.ToRequest(preset.Name, args.Theme));
        await ReloadAsync(saved.Id);
        Snackbar.Add(L[stored.IsGlobal ? "Saved as copy {0}" : "Theme {0} saved", saved.Name], Severity.Success);
    });

    private Task CancelAsync() => RunAsync(async () =>
    {
        await ThemeService.LoadAsync();
        await ReloadAsync(_selected?.Id);
    });

    private Task SetDefaultAsync() => RunAsync(async () =>
    {
        await Api.SetDefaultThemeAsync(_selected!.Id);
        await ThemeService.LoadAsync();
        await ReloadAsync(_selected.Id);
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }
    }
}
