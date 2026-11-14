using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components;
using MudBlazor.Utilities;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Themes
{
    private List<(ThemeDto Dto, CoworkeeTheme Theme)>? _tiles;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Pages { get; set; } = null!;

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync() => _tiles = [.. (await Api.GetThemesAsync()).Select(t => (t, ThemeMapper.ToTheme(t, ThemeService.ThemeType)))];

    private static string Hex(MudColor color) => color.ToString(MudColorOutputFormats.HexA);

    private Task CreateAsync()
    {
        var presets = _tiles?.Select(t => new ThemePreset<CoworkeeTheme>(t.Dto.Name, ThemeMapper.ToTheme(t.Dto, ThemeService.ThemeType)) { Id = t.Dto.Id }).ToList() ?? [];
        var start = presets.FirstOrDefault(p => _tiles!.Any(t => t.Dto.IsDefault && Equals(t.Dto.Id, p.Id)))?.Theme ?? ThemeMapper.Create(ThemeService.ThemeType);
        start.IsPublished = false;
        return OpenAsync(L["New theme"], string.Empty, start, presets, (name, theme) => Api.CreateThemeAsync(ThemeMapper.ToRequest(name, theme)));
    }

    // a built-in theme is changed in place for the organisation; it keeps its name
    private Task EditAsync(ThemeDto dto) => OpenAsync(L["Edit theme"], dto.Name, ThemeMapper.ToTheme(dto, ThemeService.ThemeType), null,
        (name, theme) => Api.UpdateThemeAsync(dto.Id, ThemeMapper.ToRequest(name, theme)), fixedName: dto.IsGlobal);

    private async Task OpenAsync(string title, string name, CoworkeeTheme theme, List<ThemePreset<CoworkeeTheme>>? presets, Func<string, CoworkeeTheme, Task<ThemeDto>> save,
        bool fixedName = false)
    {
        var parameters = new DialogParameters
        {
            { nameof(ThemeEditDialog<CoworkeeTheme>.Name), name },
            { nameof(ThemeEditDialog<CoworkeeTheme>.FixedName), fixedName },
            { nameof(ThemeEditDialog<CoworkeeTheme>.Theme), theme },
            { nameof(ThemeEditDialog<CoworkeeTheme>.Presets), presets },
            { nameof(ThemeEditDialog<CoworkeeTheme>.Save), save },
        };
        var options = await EditDialogExtensions.SideSheetAsync();
        options.MaxWidth = MaxWidth.Large;
        var dialog = await Dialogs.ShowExAsync(typeof(ThemeEditDialog<>).MakeGenericType(ThemeService.ThemeType), title, parameters, options);
        var result = await dialog.Result;
        // drops an unsaved preview, shows a saved one
        await ThemeService.LoadAsync();
        if (result is { Canceled: false, Data: ThemeDto saved })
        {
            Snackbar.Add(L["Theme {0} saved", saved.Name], Severity.Success);
            await ReloadAsync();
        }
    }

    private async Task DeleteAsync(ThemeDto dto)
    {
        if (await Dialogs.ConfirmAsync(L["Delete theme"], L["Delete the theme {0}? Users who chose it get the default theme.", dto.Name], L["Delete"], L["Cancel"],
                Icons.Material.Outlined.Delete)
            && await Snackbar.RunAsync(() => Api.DeleteThemeAsync(dto.Id), L["Theme {0} deleted", dto.Name]))
        {
            await ThemeService.LoadAsync();
            await ReloadAsync();
        }
    }

    private async Task SetPublishedAsync(ThemeDto dto, bool published)
    {
        if (await Snackbar.RunAsync(() => Api.SetThemePublishedAsync(dto.Id, published), L[published ? "Theme {0} published" : "Theme {0} unpublished", dto.Name]))
        {
            await ReloadAsync();
        }
    }

    private async Task ResetAsync(ThemeDto dto)
    {
        if (await Dialogs.ConfirmAsync(L["Reset theme"], L["Reset {0} to its built-in look? The changes of this organisation are discarded.", dto.Name], L["Reset"], L["Cancel"],
                Icons.Material.Outlined.SettingsBackupRestore)
            && await Snackbar.RunAsync(() => Api.ResetThemeAsync(dto.Id), L["Theme {0} reset", dto.Name]))
        {
            await ThemeService.LoadAsync();
            await ReloadAsync();
        }
    }

    private async Task SetDefaultAsync(ThemeDto dto)
    {
        if (await Snackbar.RunAsync(() => Api.SetDefaultThemeAsync(dto.Id)))
        {
            await ThemeService.LoadAsync();
            await ReloadAsync();
        }
    }
}
