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

    [Fact]
    public async Task Deleting_the_first_rule_shows_the_remaining_rule()
    {
        var cut = Open("a { color: red; }\nb { margin: 0; }", _ => { });

        var second = cut.FindComponents<MudExStyleEditor>()[1].Instance;

        await cut.FindAll("[data-testid='css-delete-rule']")[0].ClickAsync(new());

        cut.FindComponent<MudExStyleEditor>().Instance.ShouldBeSameAs(second);
        cut.Find("input[data-testid='css-selector']").GetAttribute("value").ShouldBe("b");
        cut.FindComponent<MudExStyleEditor>().Instance.Value.ShouldBe("margin: 0;");
    }

    [Fact]
    public async Task Moving_a_rule_down_shows_the_new_order()
    {
        var cut = Open("a { color: red; }\nb { margin: 0; }", _ => { });

        await cut.FindAll("[data-testid='css-move-down']")[0].ClickAsync(new());

        cut.FindAll("input[data-testid='css-selector']").Select(i => i.GetAttribute("value")).ShouldBe(["b", "a"]);
        cut.FindComponents<MudExStyleEditor>().Select(e => e.Instance.Value).ShouldBe(["margin: 0;", "color: red;"]);
    }

    [Fact]
    public async Task A_rule_with_declarations_and_no_selector_is_flagged_and_not_emitted()
    {
        string? received = "unset";
        var cut = Open("a { color: red; }", v => received = v);

        await cut.Find("input[data-testid='css-selector']").InputAsync(new() { Value = "" });

        received.ShouldBe("unset");
        cut.Markup.ShouldContain("Selector required");
    }

    [Fact]
    public async Task A_new_rule_without_selector_or_declarations_is_skipped()
    {
        string? received = "unset";
        var cut = Open("a { color: red; }", v => received = v);

        await cut.Find("[data-testid='css-add-rule']").ClickAsync(new());
        await cut.FindAll("[data-testid='css-move-up']")[1].ClickAsync(new());

        received.ShouldBe("a {\n  color: red;\n}");
    }

    [Fact]
    public async Task Whitespace_only_raw_css_is_emitted_as_null()
    {
        string? received = "unset";
        var cut = Open("a { color: red; }", v => received = v);
        await cut.Find("[data-testid='css-mode-raw']").ClickAsync(new());

        await cut.InvokeAsync(() => cut.FindComponent<CodeEditor>().Instance.SetFromEditorAsync("  \n "));

        received.ShouldBeNull();
    }

    [Fact]
    public async Task Whitespace_typed_into_the_raw_editor_stays_in_the_editor()
    {
        var cut = Open("a { color: red; }", _ => { });
        await cut.Find("[data-testid='css-mode-raw']").ClickAsync(new());

        await cut.InvokeAsync(() => cut.FindComponent<CodeEditor>().Instance.SetFromEditorAsync("  \n "));

        cut.FindComponent<CodeEditor>().Instance.Value.ShouldBe("  \n ");
    }

    [Fact]
    public async Task Switching_to_raw_with_a_pending_rule_shows_a_hint()
    {
        var cut = Open("a { color: red; }", v => { });
        await cut.Find("input[data-testid='css-selector']").InputAsync(new() { Value = "" });
        cut.FindAll("[data-testid='css-pending-rule']").ShouldBeEmpty();

        await cut.Find("[data-testid='css-mode-raw']").ClickAsync(new());

        cut.Find("[data-testid='css-pending-rule']").TextContent.ShouldContain("not saved yet");
    }

    [Fact]
    public async Task Filling_a_blank_selector_emits_again()
    {
        string? received = "unset";
        var cut = Open("a { color: red; }", v => received = v);
        await cut.Find("input[data-testid='css-selector']").InputAsync(new() { Value = "" });
        received.ShouldBe("unset");

        await cut.Find("input[data-testid='css-selector']").InputAsync(new() { Value = "b" });

        received.ShouldBe("b {\n  color: red;\n}");
    }

    [Fact]
    public async Task Read_only_hides_the_buttons_and_ignores_the_style_editor()
    {
        string? received = "unset";
        Render<MudPopoverProvider>();
        var cut = Render<CssRulesEditor>(p => p
            .Add(e => e.Value, "a { color: red; }")
            .Add(e => e.ReadOnly, true)
            .Add(e => e.ValueChanged, v => received = v));

        cut.FindAll("[data-testid='css-delete-rule']").ShouldBeEmpty();
        cut.FindAll("[data-testid='css-add-rule']").ShouldBeEmpty();
        await cut.InvokeAsync(() => cut.FindComponent<MudExStyleEditor>().Instance.ValueChanged.InvokeAsync("margin: 0;"));

        received.ShouldBe("unset");
    }
}