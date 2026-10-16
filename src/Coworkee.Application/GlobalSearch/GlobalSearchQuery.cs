namespace Coworkee.Application.GlobalSearch;

/// <summary>
/// The typed search: <c>max</c> finds by prefix and part, <c>"Max Mustermann"</c> only exact values, <c>users:max</c> or <c>type:users max</c>
/// only in one provider, a GUID the record with that id.
/// </summary>
public sealed record GlobalSearchQuery(string Text)
{
    public const int MaxLength = 200;

    /// <summary>Provider names to search in; empty searches all.</summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>The text was quoted: whole values only.</summary>
    public bool Exact { get; init; }

    public Guid? Id { get; init; }

    /// <summary>The words of <see cref="Text"/>; a record matches when each word is found in one of its fields.</summary>
    public IReadOnlyList<string> Words { get; init; } = [];

    /// <param name="isType">Whether a word before a colon names a provider; other colons stay part of the text (<c>10:30</c>).</param>
    public static GlobalSearchQuery Parse(string? raw, Func<string, bool> isType)
    {
        var types = new List<string>();
        var text = new List<string>();
        foreach (var token in (raw ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = token.IndexOf(':');
            var head = colon > 0 ? token[..colon] : null;
            if (head is not null && string.Equals(head, "type", StringComparison.OrdinalIgnoreCase) && colon < token.Length - 1)
            {
                types.Add(token[(colon + 1)..].ToLowerInvariant());
            }
            else if (head is not null && isType(head))
            {
                types.Add(head.ToLowerInvariant());
                if (colon < token.Length - 1)
                {
                    text.Add(token[(colon + 1)..]);
                }
            }
            else
            {
                text.Add(token);
            }
        }

        var joined = string.Join(' ', text);
        if (joined.Length > MaxLength)
        {
            joined = joined[..MaxLength];
        }

        var exact = joined.Length > 1 && joined[0] == '"' && joined[^1] == '"';
        if (exact)
        {
            joined = joined[1..^1].Trim();
        }

        return new GlobalSearchQuery(joined)
        {
            Types = types.Distinct().ToList(),
            Exact = exact,
            Id = Guid.TryParse(joined, out var id) ? id : null,
            Words = exact ? [joined] : joined.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        };
    }

    /// <summary>A type word names a provider by its name or group, or starts its name (<c>user</c>, <c>prod</c>).</summary>
    public static bool Names(string word, string name, string group) =>
        string.Equals(word, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(word, group, StringComparison.OrdinalIgnoreCase)
        || (word.Length >= 3 && name.StartsWith(word, StringComparison.OrdinalIgnoreCase));
}
