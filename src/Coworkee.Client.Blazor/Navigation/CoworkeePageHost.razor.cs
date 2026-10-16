using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// Serves the library's pages: the router comes here for every address no page of the app has. Old addresses go on to the current ones,
/// unknown ones to the router's NotFoundPage or a short text.
/// </summary>
public partial class CoworkeePageHost
{
    private RouteData? _routeData;
    private bool _notFound;

    [Inject] private CoworkeePages Pages { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string? Path { get; set; }

    protected override void OnParametersSet()
    {
        _routeData = null;
        _notFound = false;
        var match = Pages.Match(Nav.ToBaseRelativePath(Nav.Uri));
        if (match?.RedirectTo is { } target)
        {
            Nav.NavigateTo(target, replace: true);
        }
        else if (match is null)
        {
            // a router with a NotFoundPage shows it; the NotFound fragment of older apps is out of reach, so the host says it itself
            _notFound = true;
            Nav.NotFound();
        }
        else
        {
            _routeData = new RouteData(match.Component, match.Values);
        }
    }
}
