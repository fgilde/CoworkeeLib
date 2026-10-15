namespace Coworkee.Client.Blazor.CommandPalette;

public sealed record FuzzyResult(int Score, IReadOnlyList<int> Positions);

/// <summary>Matches typed letters in order ("nwprd" finds "New product"); a plain substring and word starts score higher.</summary>
public static class FuzzyMatch
{
    public static FuzzyResult? Match(string query, string text)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
        {
            return new FuzzyResult(0, []);
        }

        var index = text.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        if (index >= 0)
        {
            return new FuzzyResult(1000 - index + (IsWordStart(text, index) ? 200 : 0), [.. Enumerable.Range(index, needle.Length)]);
        }

        var positions = new List<int>();
        var score = 0;
        var at = 0;
        foreach (var letter in needle.Where(c => !char.IsWhiteSpace(c)))
        {
            while (at < text.Length && char.ToLowerInvariant(text[at]) != char.ToLowerInvariant(letter))
            {
                at++;
            }

            if (at == text.Length)
            {
                return null;
            }

            score += IsWordStart(text, at) ? 10 : positions.Count > 0 && positions[^1] == at - 1 ? 5 : 1;
            positions.Add(at++);
        }

        return new FuzzyResult(score - (positions[^1] - positions[0]) / 4, positions);
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]) || (char.IsUpper(text[index]) && char.IsLower(text[index - 1]));
}
