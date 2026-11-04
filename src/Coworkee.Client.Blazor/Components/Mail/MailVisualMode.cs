using System.Text.RegularExpressions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>
/// The visual editor rewrites markup, so it only edits the tags, attributes and styles TipTap and Quill keep (and write themselves);
/// everything else, Scriban logic included, opens as a protected chip (<see cref="MailMentionHtml"/>). Only what no chip can hold
/// opens in the source: a whole document, or an element that is never closed.
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

    public static bool Allowed(string body) => !Document().IsMatch(body) && MailMentionHtml.Protectable(body);

    /// <summary>Whether the editor keeps a tag with these attributes as it is.</summary>
    internal static bool Kept(string name, string attributes) =>
        Tags.Contains(name) && Attribute().Matches(attributes).All(a => KeptAttribute(a.Groups["name"].Value, Unquote(a.Groups["value"].Value)));

    private static bool KeptAttribute(string attribute, string value) => attribute.ToLowerInvariant() switch
    {
        _ when attribute.StartsWith("data-", StringComparison.OrdinalIgnoreCase) => true,
        "style" => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(d => Styles.Contains(d.Split(':')[0].Trim())),
        // mentions, and the classes Quill writes itself (ql-align-center, ql-indent-1)
        "class" => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(c => c == "mention" || c.StartsWith("ql-", StringComparison.Ordinal)),
        _ => Attributes.Contains(attribute),
    };

    private static string Unquote(string value) => value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;

    // the head and the body of a layout have no place in an editor of the content
    [GeneratedRegex(@"<(?:html|head|body)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Document();

    [GeneratedRegex("""(?<name>[^\s=/"']+)(?:\s*=\s*(?<value>"[^"]*"|'[^']*'|[^\s>]+))?""")]
    private static partial Regex Attribute();
}
