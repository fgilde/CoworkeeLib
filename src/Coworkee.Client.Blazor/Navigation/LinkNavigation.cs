using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// Whether the current navigation came from a clicked link. The router loads an intercepted link that matches no page from the server
/// (e.g. /bff/login); the library's catch-all page matches every address, so <see cref="CoworkeePageHost"/> does it with this.
/// Created by the layout, so it sees the navigations after the first page.
/// </summary>
public sealed class LinkNavigation : IDisposable
{
    private readonly IDisposable _registration;

    public LinkNavigation(NavigationManager nav) =>
        _registration = nav.RegisterLocationChangingHandler(context =>
        {
            Intercepted = context.IsNavigationIntercepted;
            return ValueTask.CompletedTask;
        });

    public bool Intercepted { get; private set; }

    public void Dispose() => _registration.Dispose();
}
