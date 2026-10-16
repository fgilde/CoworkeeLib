using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Mail;

public enum MailDiffKind
{
    Same,
    Removed,
    Added,
}

public sealed record MailDiffLine(MailDiffKind Kind, string Text);

/// <summary>Line diff of two template bodies; HTML on one line is split between adjacent tags so a changed paragraph shows on its own.</summary>
public static partial class MailLineDiff
{
    // ponytail: plain LCS, O(lines²) memory; fine for mail templates, Myers diff if bodies grow to thousands of lines
    public static IReadOnlyList<MailDiffLine> Compute(string before, string after)
    {
        var a = Lines(before);
        var b = Lines(after);
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<MailDiffLine>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                result.Add(new(MailDiffKind.Same, a[x++]));
                y++;
            }
            else if (x < a.Length && (y == b.Length || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                result.Add(new(MailDiffKind.Removed, a[x++]));
            }
            else
            {
                result.Add(new(MailDiffKind.Added, b[y++]));
            }
        }

        return result;
    }

    private static string[] Lines(string text) => text.Length == 0 ? [] : [.. Split().Split(text.Replace("\r\n", "\n")).Where(l => l.Trim().Length > 0)];

    [GeneratedRegex(@"\n|(?<=>)(?=<)")]
    private static partial Regex Split();
}
