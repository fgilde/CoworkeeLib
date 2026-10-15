namespace Coworkee.OData.Transfer.Import;

/// <summary>Suggests a field per column: same name, label or translated label, ignoring case, spaces and punctuation; each field once.</summary>
public static class ImportColumnMatcher
{
    public static IReadOnlyList<string?> Suggest(IReadOnlyList<string> headers, IReadOnlyList<ImportField> fields, Func<string, string> translate)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        return
        [
            .. headers.Select(header =>
            {
                var key = Normalize(header);
                var field = fields.FirstOrDefault(f => !taken.Contains(f.Name) && Names(f, translate).Contains(key));
                if (field is not null)
                {
                    taken.Add(field.Name);
                }

                return field?.Name;
            }),
        ];
    }

    private static HashSet<string> Names(ImportField field, Func<string, string> translate) =>
    [
        Normalize(field.Name),
        Normalize(field.Label),
        Normalize(translate(field.Label)),
        .. field.Lookup is null ? [] : new[] { Normalize(field.Name[..^2]) },
    ];

    private static string Normalize(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
