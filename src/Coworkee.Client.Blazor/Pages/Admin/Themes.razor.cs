using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;
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

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync() => _tiles = [.. (await Api.GetThemesAsync()).Select(t => (t, ThemeMapper.ToTheme(t)))];

    private static string Hex(MudColor color) => color.ToString(MudColorOutputFormats.HexA);

    private Task CreateAsync()
    {
        var presets = _tiles?.Select(t => new ThemePreset<CoworkeeTheme>(t.Dto.Name, ThemeMapper.ToTheme(t.Dto)) { Id = t.Dto.Id }).ToList() ?? [];
        var start = presets.FirstOrDefault(p => _tiles!.Any(t => t.Dto.IsDefault && Equals(t.Dto.Id, p.Id)))?.Theme ?? new CoworkeeTheme();
        start.IsPublished = false;
        return OpenAsync(L["New theme"], string.Empty, start, presets, (name, theme) => Api.CreateThemeAsync(ThemeMapper.ToRequest(name, theme)));
    }

    private Task EditAsync(ThemeDto dto) => OpenAsync(L["Edit theme"], dto.IsGlobal ? L["{0} copy", dto.Name] : dto.Name, ThemeMapper.ToTheme(dto), null,
        (name, theme) => dto.IsGlobal ? Api.CreateThemeAsync(ThemeMapper.ToRequest(name, theme)) : Api.UpdateThemeAsync(dto.Id, ThemeMapper.ToRequest(name, theme)));

    private async Task OpenAsync(string title, string name, CoworkeeTheme theme, List<ThemePreset<CoworkeeTheme>>? presets, Func<string, CoworkeeTheme, Task<ThemeDto>> save)
    {
        var parameters = new DialogParameters
        {
            { nameof(ThemeEditDialog.Name), name },
            { nameof(ThemeEditDialog.Theme), theme },
            { nameof(ThemeEditDialog.Presets), presets },
            { nameof(ThemeEditDialog.Save), save },
        };
        var dialog = await Dialogs.ShowSideSheetAsync<ThemeEditDialog>(title, parameters, o => o.MaxWidth = MaxWidth.Large);
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

    private async Task SetDefaultAsync(ThemeDto dto)
    {
        if (await Snackbar.RunAsync(() => Api.SetDefaultThemeAsync(dto.Id)))
        {
            await ThemeService.LoadAsync();
            await ReloadAsync();
        }
    }
}
