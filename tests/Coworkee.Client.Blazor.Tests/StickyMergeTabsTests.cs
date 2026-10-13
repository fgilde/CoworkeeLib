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

    private IRenderedComponent<StickyMergeTabs> RenderTabs() => Render<StickyMergeTabs>(parameters => parameters
        .Add(p => p.ChildContent, (RenderFragment)(builder =>
        {
            builder.OpenComponent<MudTabs>(0);
            builder.AddAttribute(1, nameof(MudTabs.ChildContent), (RenderFragment)(panels =>
            {
                panels.OpenComponent<MudTabPanel>(0);
                panels.AddAttribute(1, nameof(MudTabPanel.Text), "First");
                panels.CloseComponent();
                panels.OpenComponent<MudTabPanel>(2);
                panels.AddAttribute(3, nameof(MudTabPanel.Text), "Second");
                panels.CloseComponent();
            }));
            builder.CloseComponent();
        })));

    [Fact]
    public void It_renders_the_given_tabs_behind_the_sentinel_and_observes_once()
    {
        var tabs = RenderTabs();

        tabs.FindAll(".mud-tab").Count.ShouldBe(2);
        tabs.Find(".cw-tabs-stick-merge > .cw-tabs-sentinel + .mud-tabs > .mud-tabs-tabbar").ShouldNotBeNull();
        tabs.WaitForAssertion(() => _module.Invocations["observeStuck"].Count.ShouldBe(1));
        tabs.Render();
        _module.Invocations["observeStuck"].Count.ShouldBe(1);
    }

    [Fact]
    public void Tab_bar_and_panels_form_one_outlined_unit_with_the_stuck_bar_as_high_as_the_toolbar()
    {
        var css = RenderTabs().Find("style").TextContent;

        css.ShouldContain(".mud-tabs-tabbar { border-radius: var(--mud-default-borderradius) var(--mud-default-borderradius) 0 0; }");
        css.ShouldContain(".mud-tabs-panels { border: 1px solid var(--mud-palette-lines-default); border-top: 0;");
        css.ShouldContain("min-height: var(--cw-stuck-height");
    }

    [Fact]
    public async Task Leaving_the_page_disconnects_the_observer_once()
    {
        var tabs = RenderTabs();
        tabs.WaitForAssertion(() => _module.Invocations["observeStuck"].Count.ShouldBe(1));

        await tabs.Instance.DisposeAsync();
        await tabs.Instance.DisposeAsync();

        _observer.Invocations["dispose"].Count.ShouldBe(1);
    }
}
