namespace Coworkee.Client.Blazor.Theming;

public abstract record CssBlock;

/// <summary>A plain <c>selector { declarations }</c> rule; <paramref name="Declarations"/> is the trimmed text between the braces.</summary>
public sealed record CssRule(string Selector, string Declarations) : CssBlock;

/// <summary>Anything the rules editor does not edit (at-rules, comments, nested or unparseable CSS), kept verbatim.</summary>
public sealed record CssRawBlock(string Text) : CssBlock;

public static class CssStylesheet
{
    public static IReadOnlyList<CssBlock> Parse(string? css)
    {
        if (string.IsNullOrWhiteSpace(css)) return [];
        var blocks = new List<CssBlock>();
        var i = 0;
        while (true)
        {
            while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
            if (i >= css.Length) return blocks;
            var block = ReadBlock(css, ref i);
            if (block is null) return [new CssRawBlock(css.Trim())];
            blocks.Add(block);
        }
    }

    public static string? Serialize(IEnumerable<CssBlock> blocks)
    {
        var parts = blocks.Select(b => b switch
        {
            CssRule r => SerializeRule(r),
            CssRawBlock raw => raw.Text,
            _ => string.Empty
        }).ToList();
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static string SerializeRule(CssRule rule)
    {
        var lines = SplitTopLevel(rule.Declarations, ';')
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Select(d => $"  {d};\n");
        return $"{rule.Selector} {{\n{string.Concat(lines)}}}";
    }

    private static CssBlock? ReadBlock(string css, ref int i)
    {
        var start = i;
        if (css.AsSpan(i).StartsWith("/*"))
        {
            var end = Skip(css, i);
            if (end < 0) return null;
            i = end;
            return new CssRawBlock(css[start..i]);
        }

        var atRule = css[i] == '@';
        var open = -1;
        while (i < css.Length && open < 0)
        {
            var skipped = Skip(css, i);
            if (skipped < 0) return null;
            if (skipped > i) { i = skipped; continue; }
            switch (css[i])
            {
                case '{': open = i; break;
                case '}': return null;
                case ';' when atRule: i++; return new CssRawBlock(css[start..i]);
                case ';': return null;
            }
            if (open < 0) i++;
        }
        if (open < 0) return null;

        var nested = false;
        var depth = 0;
        for (i = open; i < css.Length; i++)
        {
            var skipped = Skip(css, i);
            if (skipped < 0) return null;
            if (skipped > i) { i = skipped - 1; continue; }
            if (css[i] == '{') { if (++depth > 1) nested = true; }
            else if (css[i] == '}' && --depth == 0) break;
        }
        if (depth != 0) return null;

        var close = i++;
        if (atRule || nested) return new CssRawBlock(css[start..i]);
        return new CssRule(css[start..open].Trim(), css[(open + 1)..close].Trim());
    }

    /// <summary>Index after the string or comment starting at <paramref name="i"/>; <paramref name="i"/> if none starts there; -1 if unterminated.</summary>
    private static int Skip(string s, int i)
    {
        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
        {
            var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return end < 0 ? -1 : end + 2;
        }
        if (s[i] is not ('"' or '\'')) return i;
        for (var j = i + 1; j < s.Length; j++)
        {
            if (s[j] == '\\') j++;
            else if (s[j] == s[i]) return j + 1;
        }
        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string s, char separator)
    {
        var start = 0;
        var parens = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var skipped = Skip(s, i);
            if (skipped < 0) break;
            if (skipped > i) { i = skipped - 1; continue; }
            if (s[i] == '(') parens++;
            else if (s[i] == ')' && parens > 0) parens--;
            else if (s[i] == separator && parens == 0)
            {
                yield return s[start..i];
                start = i + 1;
            }
        }
        yield return s[start..];
    }
}
