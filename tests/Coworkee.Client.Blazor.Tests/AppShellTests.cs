using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The app's router with every page of the library next to pages apps have of their own, on an install that is not set up yet.</summary>
public sealed class AppShellTests : ClientTestBase
{
    [Fact]
    public void A_fresh_install_reaches_setup_with_the_library_pages_next_to_the_apps_own()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(false));
        AddAuthorization();

        // two pages with one route fail the whole route table, so the app would never get to setup
        Render<Router>(p => p
            .Add(r => r.AppAssembly, typeof(AppShellTests).Assembly)
            .Add(r => r.AdditionalAssemblies, [typeof(CoworkeeLayout).Assembly])
            .Add(r => r.Found, data => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), data);
                builder.AddAttribute(2, nameof(RouteView.DefaultLayout), typeof(CoworkeeLayout));
                builder.CloseComponent();
            }));

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/setup");
    }
}

[Route("/")]
public sealed class AppHomePage : ComponentBase;

// routes apps used for their own pages before the library had them
[Route("/admin/ai")]
public sealed class AppAiPage : ComponentBase;
