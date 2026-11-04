using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class ThemePicker
{
    internal static readonly (string Mode, string Label, string Icon)[] Modes =
    [
        ("light", "Light", Icons.Material.Outlined.LightMode),
        ("system", "System", Icons.Material.Outlined.Contrast),
        ("dark", "Dark", Icons.Material.Outlined.DarkMode),
    ];

    [Parameter] public IReadOnlyList<ThemeDto>? Themes { get; set; }

    [Parameter] public Guid? SelectedId { get; set; }

    [Parameter] public EventCallback<Guid> SelectedIdChanged { get; set; }

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string Mode { get; set; } = "system";

    [Parameter] public EventCallback<string> ModeChanged { get; set; }

    /// <summary>More actions next to the mode buttons.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private Task KeyAsync(KeyboardEventArgs e, ThemeDto theme) => e.Key is "Enter" or " " ? SelectedIdChanged.InvokeAsync(theme.Id) : Task.CompletedTask;

    // what the palette leaves unset falls back to MudBlazor's defaults; "system" previews the light palette
    private PreviewColors Preview(ThemeDto theme)
    {
        var dark = Mode == "dark";
        string Of(string key, string fallback) => ThemeColors.Of(theme, key, dark) ?? fallback;
        var primary = Of("Primary", "#594ae2");
        return new PreviewColors(
            Of("AppbarBackground", primary),
            Of("AppbarText", "#ffffff"),
            Of("Background", dark ? "#32333d" : "#ffffff"),
            Of("DrawerBackground", dark ? "#27272f" : "#f5f5f5"),
            Of("Surface", dark ? "#373740" : "#ffffff"),
            Of("TextPrimary", dark ? "rgba(255,255,255,0.70)" : "rgba(66,66,66,1)"),
            Of("TextSecondary", dark ? "rgba(255,255,255,0.50)" : "rgba(0,0,0,0.54)"),
            primary,
            Of("Secondary", "#ff4081"));
    }
}
