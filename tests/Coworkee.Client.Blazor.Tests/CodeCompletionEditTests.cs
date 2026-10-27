using Coworkee.Client.Blazor.Components.Editors;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CodeCompletionEditTests
{
    private static readonly CodeCompletion Name = new("user.first_name", "{{ user.first_name }}");

    // applies the edit the way Monaco does: the range is replaced by the insert text
    private static string Pick(string lineWithCursor, CodeCompletion completion)
    {
        var column = lineWithCursor.IndexOf('|') + 1;
        var line = lineWithCursor.Replace("|", string.Empty);
        var (start, end, _) = CodeCompletionEdit.For(line, column, "{{", completion);
        return line[..(start - 1)] + completion.InsertText + line[(end - 1)..];
    }

    [Theory]
    [InlineData("<p>Hi {{|</p>", "<p>Hi {{ user.first_name }}</p>")]
    [InlineData("{{|}}", "{{ user.first_name }}")] // Monaco closed both braces
    [InlineData("{|}", "{{ user.first_name }}")] // the list opens at the first brace already
    [InlineData("Hi {{ us|}} there", "Hi {{ user.first_name }} there")]
    [InlineData("Hi {{ us| }}", "Hi {{ user.first_name }}")]
    [InlineData("Hi |", "Hi {{ user.first_name }}")] // Ctrl+Space without a trigger
    [InlineData("Hi us|", "Hi {{ user.first_name }}")]
    [InlineData("<a href=\"|\">", "<a href=\"{{ user.first_name }}\">")]
    public void A_pick_replaces_the_trigger_the_word_and_the_auto_closed_braces(string line, string expected) =>
        Pick(line, Name).ShouldBe(expected);

    [Fact]
    public void Closing_brackets_the_completion_does_not_end_with_stay()
    {
        Pick("f(|)", Name).ShouldBe("f({{ user.first_name }})");
    }

    [Fact]
    public void Snippet_marks_do_not_hide_the_closing_braces()
    {
        Pick("{{|}}", new CodeCompletion("if", "{{ if $1 }}", Snippet: true)).ShouldBe("{{ if $1 }}");
    }

    [Fact]
    public void The_filter_text_starts_with_what_was_typed_of_the_trigger()
    {
        CodeCompletionEdit.For("{{ us", 6, "{{", Name).FilterText.ShouldBe("{{ user.first_name");
        CodeCompletionEdit.For("Hi us", 6, "{{", Name).FilterText.ShouldBe("user.first_name");
    }

    [Fact]
    public void Highlights_are_found_on_every_line()
    {
        var ranges = CodeHighlights.Of("<p>{{ a }}</p>\r\n<p>x {{ b.c }}</p>", @"\{\{.*?\}\}");

        ranges.Select(r => (r.StartLineNumber, r.StartColumn, r.EndLineNumber, r.EndColumn)).ShouldBe([(1, 4, 1, 11), (2, 6, 2, 15)]);
    }
}
