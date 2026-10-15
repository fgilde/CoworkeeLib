using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PhoneLayoutTests : ClientTestBase
{
    public PhoneLayoutTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        var viewport = Substitute.For<IBrowserViewportService>();
        viewport.SubscribeAsync(Arg.Any<Guid>(), Arg.Any<Action<BrowserViewportEventArgs>>(), Arg.Any<ResizeOptions?>(), Arg.Any<bool>()).Returns(call =>
        {
            call.Arg<Action<BrowserViewportEventArgs>>()(new BrowserViewportEventArgs(Guid.Empty, new BrowserWindowSize { Width = 390, Height = 844 }, Breakpoint.Xs, true));
            return Task.CompletedTask;
        });
        Services.AddSingleton(viewport);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public async Task On_a_phone_the_app_bar_folds_its_actions_into_one_menu_and_the_navigation_closes_after_a_click()
    {
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Find("[data-testid='app-bar-more']"));
        layout.FindAll("[data-testid='rtl-toggle']").ShouldBeEmpty();
        layout.Find("[data-testid='nav-drawer']").ClassList.ShouldContain("mud-drawer-temporary");

        await OpenNavigationAsync(layout);
        await layout.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog/products"));

        layout.WaitForAssertion(() => layout.Find("[data-testid='nav-drawer']").ClassList.ShouldContain("mud-drawer--closed"));
    }
}
