namespace Coworkee.Client.Blazor.Navigation;

/// <summary>Route templates of library pages: literal segments and parameters like {Name} or {Id:guid}.</summary>
internal static class PageRoute
{
    public static bool TryMatch(string template, string[] segments, out IReadOnlyDictionary<string, object?> values)
    {
        var parts = template.Length == 0 ? [] : template.Split('/');
        var found = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        values = found;
        if (parts.Length != segments.Length)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].StartsWith('{'))
            {
                if (!parts[i].Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                continue;
            }

            var (name, constraint) = Parameter(parts[i]);
            if (constraint == "guid")
            {
                if (!Guid.TryParse(segments[i], out var id))
                {
                    return false;
                }

                found[name] = id;
            }
            else
            {
                found[name] = segments[i];
            }
        }

        return true;
    }

    public static string Fill(string template, IReadOnlyDictionary<string, object?> values) =>
        string.Join('/', template.Split('/').Select(part => part.StartsWith('{')
            ? Uri.EscapeDataString(values.TryGetValue(Parameter(part).Name, out var value) ? value?.ToString() ?? string.Empty : string.Empty)
            : part));

    private static (string Name, string? Constraint) Parameter(string part)
    {
        var inner = part.Trim('{', '}').TrimEnd('?');
        var colon = inner.IndexOf(':');
        return colon < 0 ? (inner, null) : (inner[..colon], inner[(colon + 1)..].ToLowerInvariant());
    }
}
