using System.Reflection;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// MudExThemeEdit for one theme of the app's theme class <typeparamref name="TTheme"/> with a live preview; closes with the saved theme, stays open with
/// the API's messages.
/// </summary>
public partial class ThemeEditDialog<TTheme> where TTheme : CoworkeeTheme
{
    // besides its simple list MudEx shows only the properties TTheme declares itself; those of CoworkeeTheme belong there too when an app derives from it
    private static readonly string[] SimpleMode =
    [
        .. new MudExThemeEdit<TTheme>().PropertiesForSimpleMode,
        .. typeof(CoworkeeTheme).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name),
    ];

    private List<ThemePreset<TTheme>>? _presets;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string Name { get; set; } = string.Empty;

    [Parameter, EditorRequired] public CoworkeeTheme Theme { get; set; } = null!;

    /// <summary>Themes to start a new one from.</summary>
    [Parameter] public List<ThemePreset<CoworkeeTheme>>? Presets { get; set; }

    [Parameter, EditorRequired] public Func<string, CoworkeeTheme, Task<ThemeDto>> Save { get; set; } = null!;

    protected override void OnParametersSet() =>
        _presets = Presets?.Select(p => new ThemePreset<TTheme>(p.Name, (TTheme)p.Theme) { Id = p.Id }).ToList();

    private async Task SaveAsync(ThemeChangedArgs<TTheme> args)
    {
        ThemeDto? saved = null;
        if (await Snackbar.RunAsync(async () => saved = await Save(Name.Trim(), args.Theme)))
        {
            Dialog.Close(DialogResult.Ok(saved));
        }
    }

    private void Cancel() => Dialog.Cancel();
}
