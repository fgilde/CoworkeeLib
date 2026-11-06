using Coworkee.Contracts.Settings;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// The app bars of the installation: the first at <paramref name="Position"/>, an optional second one at <paramref name="Second"/>
/// (settings <see cref="HeaderSettings.Position"/> and <see cref="HeaderSettings.SecondPosition"/>).
/// </summary>
public sealed record HeaderLayout(HeaderPosition Position = HeaderPosition.Top, HeaderPosition? Second = null)
{
    public static HeaderLayout Default { get; } = new();

    public static HeaderLayout From(IReadOnlyDictionary<string, string?> settings)
    {
        var position = Parse(settings.GetValueOrDefault(HeaderSettings.Position)) ?? HeaderPosition.Top;
        var second = Parse(settings.GetValueOrDefault(HeaderSettings.SecondPosition));
        // two bars on one edge would cover each other
        return new HeaderLayout(position, second == position ? null : second);
    }

    /// <summary>The place an item shows at: without a second bar its items are the first bar's.</summary>
    public HeaderPlacement Effective(HeaderPlacement placement) => (Second, placement) switch
    {
        (null, HeaderPlacement.SecondBar) => HeaderPlacement.Bar,
        (null, HeaderPlacement.SecondOverflow) => HeaderPlacement.Overflow,
        _ => placement,
    };

    /// <summary>The bar that carries the menu button and the app's name: the first one across the window, else none (the navigation carries them).</summary>
    public int? BrandBar => !IsVertical(Position) ? 0 : Second is { } second && !IsVertical(second) ? 1 : null;

    public bool Has(HeaderPosition position) => Position == position || Second == position;

    public static bool IsVertical(HeaderPosition position) => position is HeaderPosition.Left or HeaderPosition.Right;

    private static HeaderPosition? Parse(string? value) => Enum.TryParse<HeaderPosition>(value, ignoreCase: true, out var position) && Enum.IsDefined(position) ? position : null;
}
