using Coworkee.Contracts.Search;

namespace Coworkee.Application.GlobalSearch;

public sealed record SearchMatchResult(double Score, IReadOnlyList<HighlightRange> Highlights);

/// <summary>
/// Scores one value against the search the same way for every provider, so hits of different providers rank together:
/// equal 1, prefix 0.8 to 0.9, word start 0.6 to 0.7, part 0.4 to 0.5, all words somewhere 0.3; longer values lose a little.
/// </summary>
public static class SearchMatch
{
    public static SearchMatchResult? Score(string? value, GlobalSearchQuery query)
    {
        if (string.IsNullOrEmpty(value) || query.Text.Length == 0)
        {
            return null;
        }

        var text = query.Text;
        if (string.Equals(value, text, StringComparison.CurrentCultureIgnoreCase))
        {
            return new SearchMatchResult(1, [new HighlightRange(0, value.Length)]);
        }

        if (query.Exact)
        {
            return null;
        }

        var fit = Math.Min(1, (double)text.Length / value.Length) * 0.1;
        var index = value.IndexOf(text, StringComparison.CurrentCultureIgnoreCase);
        if (index >= 0)
        {
            var score = index == 0 ? 0.8 : IsWordStart(value, index) ? 0.6 : 0.4;
            return new SearchMatchResult(score + fit, [new HighlightRange(index, text.Length)]);
        }

        if (query.Words.Count < 2)
        {
            return null;
        }

        var ranges = new List<HighlightRange>();
        foreach (var word in query.Words)
        {
            var at = value.IndexOf(word, StringComparison.CurrentCultureIgnoreCase);
            if (at < 0)
            {
                return null;
            }

            ranges.Add(new HighlightRange(at, word.Length));
        }

        return new SearchMatchResult(0.3 + fit, [.. ranges.OrderBy(r => r.Start)]);
    }

    /// <summary>The best of several values; highlights only when the first value (the title) matched.</summary>
    public static SearchMatchResult? Best(GlobalSearchQuery query, string? title, params IEnumerable<string?> others)
    {
        var best = Score(title, query);
        foreach (var other in others)
        {
            if (Score(other, query) is { } match && match.Score > (best?.Score ?? 0))
            {
                // a match outside the title ranks a little lower than the same match in the title
                best = new SearchMatchResult(match.Score * 0.9, best?.Highlights ?? []);
            }
        }

        return best;
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]) || (char.IsUpper(text[index]) && char.IsLower(text[index - 1]));
}
