using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>Edits <see cref="CoworkeeTheme.Logo"/>: preview, choosing an image through <see cref="ThemeLogoDialog"/>, and back to the app's logo.</summary>
public partial class ThemeLogoEdit
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    private string? Source => ThemeLogo.ToDataUrl(Value) ?? Options.AppLogo;

    private async Task ChooseAsync()
    {
        if (await ThemeLogoDialog.ShowAsync(Dialogs) is { } logo)
        {
            await SetAsync(logo);
        }
    }

    private async Task SetAsync(string? value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }
}