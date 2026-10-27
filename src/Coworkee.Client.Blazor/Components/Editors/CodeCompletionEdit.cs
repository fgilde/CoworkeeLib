using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// The part of a line a picked <see cref="CodeCompletion"/> replaces: the typed part of the trigger (<c>{</c> or <c>{{</c>) with the spaces after it,
/// the word before the cursor, and the closing brackets right after the cursor that the completion ends with (Monaco closes <c>{</c> on its own).
/// Without a trigger before the cursor (Ctrl+Space) only the word is replaced. Columns are Monaco's, 1-based.
/// </summary>
internal static partial class CodeCompletionEdit
{
    public static (int StartColumn, int EndColumn, string FilterText) For(string line, int column, string trigger, CodeCompletion completion)
    {
        var cursor = Math.Clamp(column - 1, 0, line.Length);
        var word = WordBefore().Match(line[..cursor]).Value;
        var beforeWord = line[..(cursor - word.Length)];
        var trimmed = beforeWord.TrimEnd();
        var typed = Enumerable.Range(1, trigger.Length).Reverse().FirstOrDefault(n => trimmed.EndsWith(trigger[..n], StringComparison.Ordinal));
        var start = typed == 0 ? beforeWord.Length : trimmed.Length - typed;

        var closing = Closing().Match(line[cursor..]).Value;
        var text = completion.Snippet ? SnippetMarks().Replace(completion.InsertText, string.Empty) : completion.InsertText;
        var swallowed = Enumerable.Range(1, closing.Length).Reverse().FirstOrDefault(n => text.EndsWith(closing[..n], StringComparison.Ordinal));

        return (start + 1, cursor + swallowed + 1, line[start..beforeWord.Length] + completion.Label);
    }

    [GeneratedRegex(@"[\w.\[\]]*$")]
    private static partial Regex WordBefore();

    [GeneratedRegex(@"^\s*[)\]}]+")]
    private static partial Regex Closing();

    [GeneratedRegex(@"\$\d+|\$\{\d+(:[^}]*)?\}")]
    private static partial Regex SnippetMarks();
}
