using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>
/// Converts between the stored Scriban body (<c>{{ user.first_name }}</c>) and the mention markup of the visual editor.
/// What the editor would rewrite (Scriban logic, styled buttons, tables, any tag it does not keep) becomes a protected chip:
/// a mention whose <c>data-__data-json</c> holds the raw source, the one attribute besides the label both TipTap and Quill keep.
/// </summary>
public static partial class MailMentionHtml
{
    private const int MaxLabel = 40;

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    /// <summary>Replaces mention spans by their placeholder and protected chips by their source; a span whose value is not a known variable becomes inert text.</summary>
    public static string ToScriban(string html, IReadOnlyCollection<string> known)
    {
        var result = new System.Text.StringBuilder();
        var raws = new List<string>();
        var position = 0;
        foreach (Match start in MentionStart().Matches(html))
        {
            if (start.Index < position)
            {
                continue;
            }

            var end = EndOfSpan(html, start.Index + start.Length);
            result.Append(html, position, start.Index - position);
            if (Raw(start.Value) is { } raw)
            {
                // a private-use marker for now, so a paragraph that holds nothing but chips can be told apart below
                result.Append($"\uE000{raws.Count}\uE001");
                raws.Add(raw);
            }
            else
            {
                var value = Value().Match(start.Value) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
                result.Append(value is not null && known.Contains(value) ? $"{{{{ {value} }}}}" : Inert(html[(start.Index + start.Length)..end]));
            }

            position = end;
        }

        result.Append(html, position, html.Length - position);
        if (raws.Count == 0)
        {
            return result.ToString();
        }

        // both editors wrap a chip outside a paragraph in one of its own; unwrapped, the source keeps no empty <p> around logic or blocks
        var unwrapped = ChipsOnlyParagraph().Replace(result.ToString(), m => m.Groups["chips"].Value);
        return Marker().Replace(unwrapped, m => int.TryParse(m.Groups[1].Value, out var index) && index < raws.Count ? raws[index] : m.Value);
    }

    public static string ToMentions(string body, IReadOnlyCollection<string> known, Func<string, string>? localize = null) =>
        Walk(body, known, localize ?? (key => key)).Html;

    /// <summary>Whether every fragment the editor would rewrite can become a chip: false for an unclosed or stray element.</summary>
    internal static bool Protectable(string body) => Walk(body, [], key => key).Complete;

    public static string Mention(string name)
    {
        var encoded = WebUtility.HtmlEncode(name);
        return $"<span class=\"mention\" data-__type=\"{MailPlaceholder.MentionType}\" data-denotation-char=\"{{\" data-id=\"{encoded}\" data-value=\"{encoded}\"><span contenteditable=\"false\"><span class=\"ql-mention-denotation-char\">{{</span>{encoded}</span></span>";
    }

    private static string Chip(string raw, string label)
    {
        var encoded = WebUtility.HtmlEncode(label);
        var data = WebUtility.HtmlEncode(JsonSerializer.Serialize(new Dictionary<string, string> { ["raw"] = raw }));
        return $"<span class=\"mention\" data-__type=\"{MailPlaceholder.MentionType}\" data-denotation-char=\"{{\" data-id=\"{encoded}\" data-value=\"{encoded}\" data-__data-json=\"{data}\"><span contenteditable=\"false\"><span class=\"ql-mention-denotation-char\">{{</span>{encoded}</span></span>";
    }

    // the source of a protected chip; a placeholder picked in the editor carries its own data ({"name":...}) and has none
    private static string? Raw(string startTag)
    {
        if (Data().Match(startTag) is not { Success: true } match)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String
                ? raw.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Html, bool Complete) Walk(string body, IReadOnlyCollection<string> known, Func<string, string> localize)
    {
        var tokens = Token().Matches(body);
        var result = new System.Text.StringBuilder();
        var complete = true;
        var position = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            result.Append(body, position, token.Index - position);
            position = token.Index + token.Length;
            if (token.Groups["scriban"].Success)
            {
                result.Append(Placeholder().Match(token.Value) is { Success: true } placeholder && known.Contains(placeholder.Groups[1].Value) ? Mention(placeholder.Groups[1].Value)
                    : Logic().IsMatch(token.Value) ? Chip(token.Value, Label(token.Value[2..^2].Trim('-', '~'))) : token.Value);
                continue;
            }

            if (token.Groups["other"].Success)
            {
                result.Append(Chip(token.Value, Label(token.Value)));
                continue;
            }

            var name = token.Groups["name"].Value;
            if (token.Value[1] == '/')
            {
                complete &= MailVisualMode.Kept(name, string.Empty);
                result.Append(token.Value);
                continue;
            }

            var close = Void.Contains(name) || token.Value.EndsWith("/>", StringComparison.Ordinal) ? i : Close(tokens, i, name);
            if (MailVisualMode.Kept(name, token.Groups["attributes"].Value) && (close < 0 || OnlyItems(body, tokens, name, i, close)))
            {
                result.Append(token.Value);
                continue;
            }

            if (close < 0)
            {
                complete = false;
                result.Append(token.Value);
                continue;
            }

            position = tokens[close].Index + tokens[close].Length;
            var raw = body[token.Index..position];
            result.Append(Chip(raw, ElementLabel(name, raw, localize)));
            i = close;
        }

        return (result.Append(body, position, body.Length - position).ToString(), complete);
    }

    // index of the tag closing the element opened at tokens[open], -1 if it is never closed
    private static int Close(MatchCollection tokens, int open, string name)
    {
        var depth = 0;
        for (var k = open; k < tokens.Count; k++)
        {
            if (tokens[k].Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                depth += tokens[k].Value[1] == '/' ? -1 : 1;
                if (depth == 0)
                {
                    return k;
                }
            }
        }

        return -1;
    }

    // a list the editor can keep holds nothing but items: logic between them would land outside any item
    private static bool OnlyItems(string body, MatchCollection tokens, string name, int open, int close)
    {
        if (!name.Equals("ul", StringComparison.OrdinalIgnoreCase) && !name.Equals("ol", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var depth = 0;
        var position = tokens[open].Index + tokens[open].Length;
        for (var k = open + 1; k <= close; k++)
        {
            var token = tokens[k];
            if (depth == 0 && (!string.IsNullOrWhiteSpace(body[position..token.Index]) || (k < close && !token.Groups["name"].Value.Equals("li", StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            if (token.Groups["name"].Success && !Void.Contains(token.Groups["name"].Value))
            {
                depth += token.Value[1] == '/' ? -1 : 1;
            }

            position = token.Index + token.Length;
        }

        return true;
    }

    private static string ElementLabel(string name, string raw, Func<string, string> localize)
    {
        var kind = name.ToLowerInvariant() switch
        {
            "table" => localize("Table"),
            "ul" or "ol" => localize("List"),
            "img" => localize("Image"),
            _ when StyledLink().IsMatch(raw) => localize("Button"),
            var tag => $"<{tag}>",
        };
        var text = name.ToLowerInvariant() is "table" or "ul" or "ol" ? string.Empty : Label(WebUtility.HtmlDecode(Tags().Replace(raw, " ")));
        return text.Length == 0 ? kind : $"{kind}: {text}";
    }

    private static string Label(string text)
    {
        var label = Whitespace().Replace(text, " ").Trim();
        return label.Length > MaxLabel ? label[..MaxLabel] + "…" : label;
    }

    // index after the closing </span> of the span whose opening tag ended at <paramref name="from"/>; an unclosed span ends with its opening tag, so it cannot swallow the rest
    private static int EndOfSpan(string html, int from)
    {
        var depth = 1;
        foreach (Match tag in SpanTag().Matches(html, from))
        {
            depth += tag.Value[1] == '/' ? -1 : 1;
            if (depth == 0)
            {
                return tag.Index + tag.Length;
            }
        }

        return from;
    }

    private static string Inert(string innerWithClosingTags)
    {
        var text = Tags().Replace(innerWithClosingTags, string.Empty).Replace("\uFEFF", string.Empty);
        return text.Replace("<", "&lt;").Replace(">", "&gt;").Replace("{", "&#123;").Replace("}", "&#125;");
    }

    [GeneratedRegex("""<span\b[^>]*\sclass="mention"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex MentionStart();

    [GeneratedRegex(@"\bdata-value=""([^""]*)""")]
    private static partial Regex Value();

    [GeneratedRegex(@"\bdata-__data-json=""([^""]*)""")]
    private static partial Regex Data();

    [GeneratedRegex(@"<span\b[^>]*>|</span\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex SpanTag();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<other><!--.*?-->|<![^>]*>)|(?<tag></?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attributes>[^>]*)>)|(?<scriban>\{\{(?:(?!\{\{).)*?\}\})", RegexOptions.Singleline)]
    private static partial Regex Token();

    [GeneratedRegex(@"^\{\{\s*([^\s{}]+)\s*\}\}$")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^\{\{[-~]?\s*(?:(?:if|else|end|for|while|case|when|capture|with|func|tablerow|break|continue|ret)\b|#)")]
    private static partial Regex Logic();

    [GeneratedRegex(@"<a\b[^>]*\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex StyledLink();

    [GeneratedRegex(@"<p>(?<chips>\s*(?:\uE000\d+\uE001\s*)+)</p>")]
    private static partial Regex ChipsOnlyParagraph();

    [GeneratedRegex(@"\uE000(\d+)\uE001")]
    private static partial Regex Marker();
}
