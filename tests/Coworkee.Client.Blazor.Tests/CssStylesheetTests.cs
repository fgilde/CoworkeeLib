using Coworkee.Client.Blazor.Theming;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CssStylesheetTests
{
    [Fact]
    public void Plain_rules_become_rules() =>
        CssStylesheet.Parse("a { color: red; }\n.b{margin:0}")
            .ShouldBe([new CssRule("a", "color: red;"), new CssRule(".b", "margin:0")]);

    [Fact]
    public void At_rules_and_comments_stay_raw() =>
        CssStylesheet.Parse("/* x */\n@media (max-width: 600px) { a { color: red; } }\nb { c: d; }")
            .ShouldBe([
                new CssRawBlock("/* x */"),
                new CssRawBlock("@media (max-width: 600px) { a { color: red; } }"),
                new CssRule("b", "c: d;")
            ]);

    [Fact]
    public void Braces_in_strings_and_comments_do_not_split() =>
        CssStylesheet.Parse("a::after { content: \"}\"; } /* { */ b { x: y; }")
            .ShouldBe([
                new CssRule("a::after", "content: \"}\";"),
                new CssRawBlock("/* { */"),
                new CssRule("b", "x: y;")
            ]);

    [Fact]
    public void Unbalanced_css_falls_back_to_one_raw_block() =>
        CssStylesheet.Parse("a { color: red;").ShouldBe([new CssRawBlock("a { color: red;")]);

    [Theory]
    [InlineData("}")]
    [InlineData("a { content: \"x }")]
    [InlineData("/* open")]
    [InlineData("a b c")]
    [InlineData("@media x { a {")]
    public void Garbage_does_not_throw_and_stays_one_raw_block(string css) =>
        CssStylesheet.Parse(css).ShouldBe([new CssRawBlock(css)]);

    [Fact]
    public void At_rule_without_body_stays_raw() =>
        CssStylesheet.Parse("@import url(\"a;b.css\"); a { c: d; }")
            .ShouldBe([new CssRawBlock("@import url(\"a;b.css\");"), new CssRule("a", "c: d;")]);

    [Fact]
    public void Rule_with_nested_block_stays_raw() =>
        CssStylesheet.Parse("a { b { c: d; } }").ShouldBe([new CssRawBlock("a { b { c: d; } }")]);

    [Fact]
    public void Serialize_formats_rules_and_keeps_raw_text() =>
        CssStylesheet.Serialize([new CssRule("a", "color: red;"), new CssRawBlock("@media x { b { c: d; } }")])
            .ShouldBe("a {\n  color: red;\n}\n\n@media x { b { c: d; } }");

    [Fact]
    public void Serialized_css_round_trips_unchanged()
    {
        var s = CssStylesheet.Serialize([new CssRule("a", "color: red;"), new CssRawBlock("@media x { b { c: d; } }")])!;
        CssStylesheet.Serialize(CssStylesheet.Parse(s)).ShouldBe(s);
    }

    [Fact]
    public void Empty_input()
    {
        CssStylesheet.Parse(null).ShouldBeEmpty();
        CssStylesheet.Parse("  ").ShouldBeEmpty();
        CssStylesheet.Serialize([]).ShouldBeNull();
    }

    [Fact]
    public void Declarations_are_split_one_per_line_outside_strings_and_parentheses() =>
        CssStylesheet.Serialize([new CssRule("a", "color:red;background:url(x;y)")])
            .ShouldBe("a {\n  color:red;\n  background:url(x;y);\n}");

    [Fact]
    public void Rule_without_declarations_serializes_as_empty_block() =>
        CssStylesheet.Serialize([new CssRule(".x", "")]).ShouldBe(".x {\n}");
}
