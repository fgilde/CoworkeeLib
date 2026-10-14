using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Theming;
using MudBlazor;
using MudBlazor.Extensions.Components;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CssRulesEditorTests : ClientTestBase
{
    private IRenderedComponent<CssRulesEditor> Open(string? value, Action<string?> onChanged)
    {
        Render<MudPopoverProvider>();
        return Render<CssRulesEditor>(p => p
            .Add(e => e.Value, value)
            .Add(e => e.ValueChanged, onChanged));
    }

    [Fact]
    public void Rules_mode_lists_rules_and_raw_blocks()
    {
        var cut = Open("a { color: red; }\n@media x { b { c: d; } }", _ => { });

        var rules = cut.Find("[data-testid='css-rules']");
        cut.FindComponents<MudExStyleEditor>().Count.ShouldBe(1);
        cut.FindComponents<CodeEditor>().Count.ShouldBe(1);
        rules.QuerySelectorAll("[data-testid='css-selector']").Length.ShouldBe(1);
        cut.Find("input[data-testid='css-selector']").GetAttribute("value").ShouldBe("a");
    }

    [Fact]
    public async Task Adding_a_rule_emits_the_stylesheet()
    {
        string? received = null;
        var cut = Open(null, v => received = v);

        await cut.Find("[data-testid='css-add-rule']").ClickAsync(new());
        await cut.Find("input[data-testid='css-selector']").InputAsync(new() { Value = ".x" });

        received.ShouldBe(CssStylesheet.Serialize([new CssRule(".x", "")]));
        received.ShouldBe(".x {\n}");
    }

    [Fact]
    public async Task Deleting_a_rule_removes_it()
    {
        string? received = "unset";
        var cut = Open("a { color: red; }\nb { margin: 0; }", v => received = v);

        await cut.FindAll("[data-testid='css-delete-rule']")[0].ClickAsync(new());

        received.ShouldBe(CssStylesheet.Serialize([new CssRule("b", "margin: 0;")]));
    }

    [Fact]
    public async Task Empty_value_emits_null()
    {
        string? received = "unset";
        var cut = Open("a { color: red; }", v => received = v);

        await cut.Find("[data-testid='css-delete-rule']").ClickAsync(new());

        received.ShouldBeNull();
    }

    [Fact]
    public async Task Moving_a_rule_down_swaps_the_order()
    {
        string? received = null;
        var cut = Open("a { c: d; }\nb { e: f; }", v => received = v);

        await cut.FindAll("[data-testid='css-move-down']")[0].ClickAsync(new());

        received.ShouldBe(CssStylesheet.Serialize([new CssRule("b", "e: f;"), new CssRule("a", "c: d;")]));
    }

    [Fact]
    public async Task Raw_mode_shows_the_code_editor_with_the_whole_css()
    {
        const string css = "a { color: red; }\n@media x { b { c: d; } }";
        var cut = Open(css, _ => { });

        await cut.Find("[data-testid='css-mode-raw']").ClickAsync(new());

        var editor = cut.FindComponent<CodeEditor>();
        editor.Instance.Language.ShouldBe("css");
        editor.Instance.Value.ShouldBe(css);
        cut.FindComponents<MudExStyleEditor>().ShouldBeEmpty();
    }
}
