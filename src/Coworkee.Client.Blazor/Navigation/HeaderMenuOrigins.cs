using MudBlazor;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// Where the menu of an app bar item opens: away from the edge the bar sits on, aligned with the end of the bar the item is at (after a spacer: the far
/// end). <see cref="Components.CoworkeeAppBar"/> cascades it to every item; menus of the library and of apps read it as a cascading parameter. In a
/// right-to-left layout MudBlazor mirrors left and right.
/// </summary>
public sealed record HeaderMenuOrigins(Origin Anchor, Origin Transform)
{
    /// <summary>An item at the end of a bar at the top: the menus of the library opened so before the bars could move.</summary>
    public static HeaderMenuOrigins Default { get; } = For(HeaderPosition.Top, end: true);

    public static HeaderMenuOrigins For(HeaderPosition position, bool end) => (position, end) switch
    {
        (HeaderPosition.Top, false) => new(Origin.BottomLeft, Origin.TopLeft),
        (HeaderPosition.Top, true) => new(Origin.BottomRight, Origin.TopRight),
        (HeaderPosition.Bottom, false) => new(Origin.TopLeft, Origin.BottomLeft),
        (HeaderPosition.Bottom, true) => new(Origin.TopRight, Origin.BottomRight),
        (HeaderPosition.Left, false) => new(Origin.TopRight, Origin.TopLeft),
        (HeaderPosition.Left, true) => new(Origin.BottomRight, Origin.BottomLeft),
        (HeaderPosition.Right, false) => new(Origin.TopLeft, Origin.TopRight),
        _ => new(Origin.BottomLeft, Origin.BottomRight),
    };
}
