using Bunit;
using Coworkee.Client.Blazor.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The navigation entry of the assistant (/assistant) opens the window over the page the user was on.</summary>
public sealed class AssistantPageTests : ClientTestBase
{
    [Fact]
    public void The_assistant_link_opens_the_window_and_goes_back_instead_of_home()
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        module.SetupVoid("backOrHome");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/assistant");

        Render<Pages.Assistant>();

        Services.GetRequiredService<AssistantState>().IsOpen.ShouldBeTrue();
        module.VerifyInvoke("backOrHome");
        nav.Uri.ShouldEndWith("/assistant", Case.Insensitive, "the browser goes back itself; no navigation to the home page");
    }
}
