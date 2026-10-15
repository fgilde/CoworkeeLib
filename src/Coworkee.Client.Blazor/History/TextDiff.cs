using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.History;

public enum DiffKind
{
    Same,
    Removed,
    Added,
}

public sealed record DiffPart(DiffKind Kind, string Text);

/// <summary>A word diff of two texts, for long values in the history.</summary>
public static partial class TextDiff
{
    // ponytail: plain LCS over words, O(n*m); beyond this many words the texts show side by side without marks
    private const int MaxWords = 1500;

    public static bool IsLong(string? text) => text is { Length: > 80 } || text?.Contains('\n') == true;

    public static IReadOnlyList<DiffPart>? Compute(string? before, string? after)
    {
        var a = Words().Matches(before ?? string.Empty).Select(m => m.Value).ToArray();
        var b = Words().Matches(after ?? string.Empty).Select(m => m.Value).ToArray();
        if (a.Length > MaxWords || b.Length > MaxWords)
        {
            return null;
        }

        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var parts = new List<DiffPart>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                Add(parts, DiffKind.Same, a[x++]);
                y++;
            }
            else if (y < b.Length && (x == a.Length || lengths[x, y + 1] >= lengths[x + 1, y]))
            {
                Add(parts, DiffKind.Added, b[y++]);
            }
            else
            {
                Add(parts, DiffKind.Removed, a[x++]);
            }
        }

        return parts;
    }

    private static void Add(List<DiffPart> parts, DiffKind kind, string word)
    {
        if (parts.Count > 0 && parts[^1].Kind == kind)
        {
            parts[^1] = parts[^1] with { Text = parts[^1].Text + word };
        }
        else
        {
            parts.Add(new DiffPart(kind, word));
        }
    }

    // words with the whitespace that follows them, so joining the parts gives back the text
    [GeneratedRegex(@"\S+\s*|\s+")]
    private static partial Regex Words();
}
