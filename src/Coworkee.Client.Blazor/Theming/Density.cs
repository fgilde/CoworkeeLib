using MudBlazor;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>The theme's <see cref="CoworkeeTheme.Dense"/>, cascaded to every component; without a cascade components render dense.</summary>
public sealed record Density(bool Dense)
{
    public static Density Default { get; } = new(true);

    public Margin Margin => Dense ? Margin.Dense : Margin.Normal;

    public Size Size => Dense ? Size.Small : Size.Medium;
}
