using Bunit;
using Coworkee.Client.Blazor.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class StickyMergeTabsTests : ClientTestBase
{
    private readonly BunitJSModuleInterop _module;
    private readonly BunitJSModuleInterop _observer;

    public StickyMergeTabsTests()
    {
        _module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        _observer = _module.SetupModule(invocation => invocation.Identifier == "observeStuck");
    }

    [Fact]
    public void It_renders_the_tab_panels_and_observes_the_tab_bar_once()
    {
        var tabs = Render<StickyMergeTabs>(parameters => parameters
            .Add(p => p.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<MudTabPanel>(0);
                builder.AddAttribute(1, nameof(MudTabPanel.Text), "First");
                builder.CloseComponent();
                builder.OpenComponent<MudTabPanel>(2);
                builder.AddAttribute(3, nameof(MudTabPanel.Text), "Second");
                builder.CloseComponent();
            })));

        tabs.FindAll(".mud-tab").Count.ShouldBe(2);
        tabs.Find(".cw-tabs-stick-merge .cw-tabs-sentinel").ShouldNotBeNull();
        tabs.WaitForAssertion(() => _module.Invocations["observeStuck"].Count.ShouldBe(1));
        tabs.Render();
        _module.Invocations["observeStuck"].Count.ShouldBe(1);
    }

    [Fact]
    public async Task Leaving_the_page_disconnects_the_observer()
    {
        var tabs = Render<StickyMergeTabs>();
        tabs.WaitForAssertion(() => _module.Invocations["observeStuck"].Count.ShouldBe(1));

        await tabs.Instance.DisposeAsync();

        _observer.Invocations["dispose"].Count.ShouldBe(1);
    }
}
