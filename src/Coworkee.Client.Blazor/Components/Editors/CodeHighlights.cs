using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>The Monaco ranges (1-based lines and columns) of the matches of <see cref="CodeEditor.Highlight"/>.</summary>
internal static class CodeHighlights
{
    public static List<BlazorMonaco.Range> Of(string text, string pattern)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        return [.. Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200))
            .Where(m => m.Length > 0)
            .Select(m => (Start: Position(m.Index), End: Position(m.Index + m.Length)))
            .Select(r => new BlazorMonaco.Range(r.Start.Line, r.Start.Column, r.End.Line, r.End.Column))];

        (int Line, int Column) Position(int offset)
        {
            var line = lineStarts.BinarySearch(offset);
            line = line >= 0 ? line : ~line - 1;
            return (line + 1, offset - lineStarts[line] + 1);
        }
    }
}
