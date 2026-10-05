namespace Coworkee.Client.Blazor.Data;

public static class ODataFilter
{
    public static string? Search(string? text, IReadOnlyCollection<string> fields)
    {
        if (string.IsNullOrWhiteSpace(text) || fields.Count == 0)
        {
            return null;
        }

        var literal = Literal(text.Trim().ToLowerInvariant());
        var clauses = fields.Select(field => $"contains(tolower({field}),{literal})").ToList();
        return clauses.Count == 1 ? clauses[0] : "(" + string.Join(" or ", clauses) + ")";
    }

    public static string? And(params string?[] filters)
    {
        var parts = filters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => string.Join(" and ", parts.Select(p => p!.StartsWith('(') ? p : $"({p})")),
        };
    }

    public static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
