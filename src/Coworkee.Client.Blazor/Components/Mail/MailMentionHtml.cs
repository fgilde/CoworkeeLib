using System.Net;
using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>Converts between the stored Scriban body (<c>{{ user.first_name }}</c>) and the mention markup of the visual editor.</summary>
public static partial class MailMentionHtml
{
    /// <summary>Replaces mention spans by their placeholder; a span whose value is not a known variable becomes inert text.</summary>
    public static string ToScriban(string html, IReadOnlyCollection<string> known)
    {
        var result = new System.Text.StringBuilder();
        var position = 0;
        foreach (Match start in MentionStart().Matches(html))
        {
            if (start.Index < position)
            {
                continue;
            }

            var end = EndOfSpan(html, start.Index + start.Length);
            result.Append(html, position, start.Index - position);
            var value = Value().Match(start.Value) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
            result.Append(value is not null && known.Contains(value) ? $"{{{{ {value} }}}}" : Inert(html[(start.Index + start.Length)..end]));
            position = end;
        }

        return result.Append(html, position, html.Length - position).ToString();
    }

    public static string ToMentions(string body, IReadOnlyCollection<string> known) =>
        PlaceholderOutsideTags().Replace(body, m => m.Groups["name"] is { Success: true } name && known.Contains(name.Value) ? Mention(name.Value) : m.Value);

    public static string Mention(string name)
    {
        var encoded = WebUtility.HtmlEncode(name);
        return $"<span class=\"mention\" data-__type=\"{MailPlaceholder.MentionType}\" data-denotation-char=\"{{\" data-id=\"{encoded}\" data-value=\"{encoded}\"><span contenteditable=\"false\"><span class=\"ql-mention-denotation-char\">{{</span>{encoded}</span></span>";
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

    [GeneratedRegex(@"<span\b[^>]*>|</span\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex SpanTag();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"<[^>]*>|\{\{\s*(?<name>[^\s{}]+)\s*\}\}")]
    private static partial Regex PlaceholderOutsideTags();
}
