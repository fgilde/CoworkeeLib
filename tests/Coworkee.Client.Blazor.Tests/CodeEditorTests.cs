using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CodeEditorTests : ClientTestBase
{
    [Fact]
    public void Renders_the_label_and_a_fullscreen_button()
    {
        var cut = Render<CodeEditor>(p => p.Add(e => e.Label, "CSS"));

        cut.Markup.ShouldContain("CSS");
        cut.FindAll("[data-testid='code-editor-fullscreen']").Count.ShouldBe(1);
    }

    [Fact]
    public void Fullscreen_button_can_be_switched_off()
    {
        var cut = Render<CodeEditor>(p => p.Add(e => e.AllowFullscreen, false));

        cut.FindAll("[data-testid='code-editor-fullscreen']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_monaco_the_value_still_flows()
    {
        string? received = null;
        var cut = Render<CodeEditor>(p => p
            .Add(e => e.Value, "a{}")
            .Add(e => e.ValueChanged, v => received = v));

        cut.Markup.ShouldContain("a{}");
        await cut.InvokeAsync(() => cut.Instance.SetFromEditorAsync("b{}"));

        received.ShouldBe("b{}");
    }

    [Fact]
    public async Task Without_monaco_insert_appends_to_the_value()
    {
        string? received = null;
        var cut = Render<CodeEditor>(p => p
            .Add(e => e.Value, "a")
            .Add(e => e.ValueChanged, v => received = v));

        await cut.InvokeAsync(() => cut.Instance.InsertAsync("{{ Name }}"));

        received.ShouldBe("a{{ Name }}");
    }
}
