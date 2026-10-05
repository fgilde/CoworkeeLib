using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Themes
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<ThemeDto> _themes = [];

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync() => _themes = await Api.GetThemesAsync();

    private static IEnumerable<string> Swatches(ThemeDto theme) =>
        new[] { "Primary", "Secondary", "Background", "Surface" }
            .Select(name => theme.PaletteLight.TryGetProperty(name, out var value) ? value.GetString() : null)
            .OfType<string>();

    private Task SetDefaultAsync(ThemeDto theme) => RunAsync(async () =>
    {
        await Api.SetDefaultThemeAsync(theme.Id);
        await ThemeService.LoadAsync();
    });

    private Task CopyAsync(ThemeDto theme) => RunAsync(async () =>
    {
        var copy = await Api.CreateThemeAsync(new ThemeRequest($"{theme.Name} copy", theme.PaletteLight, theme.PaletteDark, theme.Typography, theme.LayoutProperties, theme.LogoSvg, theme.CustomCss));
        Nav.NavigateTo($"/admin/themes/{copy.Id}");
    });

    private Task DeleteAsync(ThemeDto theme) => RunAsync(() => Api.DeleteThemeAsync(theme.Id));

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

        await ReloadAsync();
    }
}
