using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>
/// The visual editor rewrites markup, so it is only offered for bodies it cannot damage: no Scriban logic, and only the tags,
/// attributes and styles TipTap and Quill keep (and write themselves). A styled button or a layout table opens in the source.
/// </summary>
public static partial class MailVisualMode
{
    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "b", "em", "i", "u", "s", "strike", "del", "span", "mark",
        "a", "ul", "ol", "li", "blockquote", "code", "pre", "sub", "sup",
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.OrdinalIgnoreCase) { "href", "target", "rel", "contenteditable" };

    private static readonly HashSet<string> Styles = new(StringComparer.OrdinalIgnoreCase) { "color", "background-color", "text-align" };

    public static bool Allowed(string body) => !Logic().IsMatch(body) && Tag().Matches(body).All(Kept);

    private static bool Kept(Match tag) =>
        Tags.Contains(tag.Groups["name"].Value)
        && Attribute().Matches(tag.Groups["attributes"].Value).All(a => Kept(a.Groups["name"].Value, Unquote(a.Groups["value"].Value)));

    private static bool Kept(string attribute, string value) => attribute.ToLowerInvariant() switch
    {
        _ when attribute.StartsWith("data-", StringComparison.OrdinalIgnoreCase) => true,
        "style" => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(d => Styles.Contains(d.Split(':')[0].Trim())),
        // mentions, and the classes Quill writes itself (ql-align-center, ql-indent-1)
        "class" => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(c => c == "mention" || c.StartsWith("ql-", StringComparison.Ordinal)),
        _ => Attributes.Contains(attribute),
    };

    private static string Unquote(string value) => value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;

    [GeneratedRegex(@"\{\{[-~]?\s*(if|for|while|case|capture|with|func|tablerow)\b")]
    private static partial Regex Logic();

    // comments and doctypes match without a name, so they count as unknown
    [GeneratedRegex(@"</?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attributes>[^>]*)>|<!")]
    private static partial Regex Tag();

    [GeneratedRegex("""(?<name>[^\s=/"']+)(?:\s*=\s*(?<value>"[^"]*"|'[^']*'|[^\s>]+))?""")]
    private static partial Regex Attribute();
}
