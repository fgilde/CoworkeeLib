using System.Net;
using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Navigation;

namespace Coworkee.Client.Blazor.Chat;

/// <summary>The server's safe message HTML for this app: profile links through the page addresses, restricted records with a localized label.</summary>
public static partial class ChatMarkup
{
    public const string Restricted = """<span class="cw-mention cw-restricted"></span>""";

    public static string Prepare(string? html, CoworkeePages links, string restrictedLabel) =>
        PersonLink().Replace(html ?? string.Empty, m => $"href=\"{WebUtility.HtmlEncode(links.Href(m.Groups[1].Value) ?? m.Groups[1].Value)}\"")
            .Replace(Restricted, $"""<span class="cw-mention cw-restricted" title="{WebUtility.HtmlEncode(restrictedLabel)}">#{WebUtility.HtmlEncode(restrictedLabel)}</span>""",
                StringComparison.Ordinal);

    [GeneratedRegex("href=\"(/people/[0-9a-fA-F-]{36})\"")]
    private static partial Regex PersonLink();
}
