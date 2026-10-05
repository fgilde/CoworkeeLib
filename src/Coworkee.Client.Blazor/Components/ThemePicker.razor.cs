using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class ThemePicker
{
    private static readonly (string Mode, string Label, string Icon)[] Modes =
    [
        ("light", "Light", Icons.Material.Outlined.LightMode),
        ("system", "System", Icons.Material.Outlined.Contrast),
        ("dark", "Dark", Icons.Material.Outlined.DarkMode),
    ];

    [Parameter] public IReadOnlyList<ThemeDto>? Themes { get; set; }

    [Parameter] public Guid? SelectedId { get; set; }

    [Parameter] public EventCallback<Guid> SelectedIdChanged { get; set; }

    [Parameter] public string Mode { get; set; } = "system";

    [Parameter] public EventCallback<string> ModeChanged { get; set; }

    /// <summary>More actions next to the mode buttons.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private Task KeyAsync(KeyboardEventArgs e, ThemeDto theme) => e.Key is "Enter" or " " ? SelectedIdChanged.InvokeAsync(theme.Id) : Task.CompletedTask;

    // only plain color values go into the style attribute
    private static IEnumerable<string> Swatch(ThemeDto theme)
    {
        foreach (var key in new[] { "Primary", "Secondary", "Background", "Surface" })
        {
            if (theme.PaletteLight.ValueKind == System.Text.Json.JsonValueKind.Object && theme.PaletteLight.TryGetProperty(key, out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String && value.GetString() is { Length: > 0 and <= 40 } color
                && color.All(c => char.IsAsciiLetterOrDigit(c) || c is '#' or '(' or ')' or ',' or '.' or ' ' or '%'))
            {
                yield return color;
            }
        }
    }
}
