using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Coworkee.Contracts.Chat;
using Ganss.Xss;

namespace Coworkee.Chat;

/// <summary>The editor HTML of a message after the sanitizer, with its mentions in one canonical form.</summary>
internal sealed record ChatContent(string Html, string Text, IReadOnlyList<ChatMentionItem> Mentions);

/// <summary>
/// Sanitizes what the editor sends (formatting, lists, quotes, code, links, mentions; nothing that runs), extracts its mentions and plain text,
/// and renders stored HTML for a reader: mentions become links, records the reader may not see a restricted chip.
/// </summary>
internal static class ChatHtml
{
    public const string MentionType = "Coworkee.Contracts.Chat.ChatMentionItem";
    private const string MentionSelector = "span.mention";

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    private static readonly HtmlParser Parser = new();
    private static readonly string[] Blocks = ["P", "LI", "BLOCKQUOTE", "PRE", "H1", "H2", "H3", "DIV"];

    public static ChatContent Parse(string html)
    {
        var document = Sanitizer.SanitizeDom(html);
        var body = document.Body!;
        var mentions = new List<ChatMentionItem>();
        foreach (var span in body.QuerySelectorAll(MentionSelector).ToList())
        {
            if (Read(span) is { } mention)
            {
                mentions.Add(mention);
                span.Replace(Canonical(document, span.GetAttribute("data-denotation-char") ?? Denotation(mention), mention));
            }
            else
            {
                span.Replace(document.CreateTextNode(span.TextContent));
            }
        }

        return new ChatContent(body.InnerHtml.Trim(), PlainText(body), mentions);
    }

    /// <summary>A plain text message (the API without formatting) as HTML.</summary>
    public static string FromText(string text) =>
        "<p>" + WebUtility.HtmlEncode(text.Trim()).ReplaceLineEndings("<br>") + "</p>";

    public static IReadOnlyList<ChatMentionItem> Mentions(string? html) =>
        string.IsNullOrEmpty(html) ? [] : [.. Parser.ParseDocument("<body>" + html).Body!.QuerySelectorAll(MentionSelector).Select(Read).OfType<ChatMentionItem>()];

    /// <param name="userName">The current name of a mentioned person, null for an unknown one.</param>
    /// <param name="visible">Whether the reader may see a mentioned record; null renders every record as restricted.</param>
    public static string Render(string? html, string text, Func<Guid, string?> userName, Func<ChatMentionItem, bool>? visible)
    {
        if (string.IsNullOrEmpty(html))
        {
            return FromText(text);
        }

        var document = Parser.ParseDocument("<body>" + html);
        foreach (var span in document.Body!.QuerySelectorAll(MentionSelector).ToList())
        {
            span.Replace(Read(span) is { } mention ? Link(document, mention, userName, visible) : document.CreateTextNode(span.TextContent));
        }

        foreach (var link in document.Body.QuerySelectorAll("a[href]:not(.cw-mention)"))
        {
            link.SetAttribute("target", "_blank");
            link.SetAttribute("rel", "noopener noreferrer nofollow");
        }

        return document.Body.InnerHtml;
    }

    private static INode Link(IDocument document, ChatMentionItem mention, Func<Guid, string?> userName, Func<ChatMentionItem, bool>? visible)
    {
        if (mention.Kind == ChatMentionItem.User && Guid.TryParse(mention.Id, out var userId))
        {
            var person = document.CreateElement("a");
            person.ClassName = "cw-mention";
            person.SetAttribute("href", $"/people/{userId}");
            person.SetAttribute("data-user", userId.ToString());
            person.TextContent = "@" + (userName(userId) ?? mention.Label);
            return person;
        }

        if (visible is null || !visible(mention))
        {
            var hidden = document.CreateElement("span");
            hidden.ClassName = "cw-mention cw-restricted";
            return hidden;
        }

        var record = document.CreateElement(mention.Url is null ? "span" : "a");
        record.ClassName = "cw-mention cw-entity";
        if (mention.Url is { } url)
        {
            record.SetAttribute("href", url);
        }

        record.SetAttribute("data-entity", mention.Provider ?? string.Empty);
        record.TextContent = "#" + mention.Label;
        return record;
    }

    private static ChatMentionItem? Read(IElement span)
    {
        if (span.GetAttribute("data-__data-json") is not { Length: > 0 } json)
        {
            return null;
        }

        try
        {
            var mention = JsonSerializer.Deserialize<ChatMentionItem>(json, JsonSerializerOptions.Web);
            return mention switch
            {
                { Kind: ChatMentionItem.User, Id: var id, Label: { Length: > 0 } } when Guid.TryParse(id, out _) => mention with { Provider = null, Url = null, Icon = null },
                { Kind: ChatMentionItem.Entity, Id: { Length: > 0 and <= 200 }, Provider: { Length: > 0 and <= 100 }, Label: { Length: > 0 } } =>
                    mention with { Url = IsLocal(mention.Url) ? mention.Url : null, Icon = null },
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // only addresses inside the app; a mention must not lead elsewhere
    private static bool IsLocal(string? url) =>
        url is { Length: > 1 and <= 500 } && url[0] == '/' && url[1] != '/' && url[1] != '\\' && !url.Contains(':', StringComparison.Ordinal);

    private static string Denotation(ChatMentionItem mention) => mention.Kind == ChatMentionItem.User ? "@" : "#";

    private static IElement Canonical(IDocument document, string denotation, ChatMentionItem mention)
    {
        var span = document.CreateElement("span");
        span.ClassName = "mention";
        span.SetAttribute("data-__type", MentionType);
        span.SetAttribute("data-denotation-char", denotation is "@" or "#" ? denotation : Denotation(mention));
        span.SetAttribute("data-id", mention.Id);
        span.SetAttribute("data-value", mention.Label);
        span.SetAttribute("data-__data-json", JsonSerializer.Serialize(mention, JsonSerializerOptions.Web));
        span.TextContent = Denotation(mention) + mention.Label;
        return span;
    }

    private static string PlainText(IElement body)
    {
        var text = new StringBuilder();
        Append(body, text);
        var plain = string.Join('\n', text.ToString().Split('\n').Select(l => l.TrimEnd())).Trim();
        while (plain.Contains("\n\n\n", StringComparison.Ordinal))
        {
            plain = plain.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return plain.Length > ChatLimits.TextLength ? plain[..(ChatLimits.TextLength - 1)] + "…" : plain;
    }

    private static void Append(INode node, StringBuilder text)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IElement { TagName: "SPAN" } span when span.ClassList.Contains("mention"):
                    text.Append(Read(span) is { Kind: ChatMentionItem.User } person ? "@" + person.Label : "#");
                    break;
                case IElement { TagName: "BR" }:
                    text.Append('\n');
                    break;
                case IElement element:
                    Append(element, text);
                    if (Blocks.Contains(element.TagName))
                    {
                        text.Append('\n');
                    }

                    break;
                default:
                    text.Append(child.TextContent);
                    break;
            }
        }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer { KeepChildNodes = true, AllowDataAttributes = true };
        // unknown wrappers keep their text, code does not
        sanitizer.RemovingTag += (_, e) =>
        {
            if (e.Tag.TagName is "SCRIPT" or "STYLE" or "TEMPLATE" or "NOSCRIPT" or "IFRAME" or "OBJECT" or "SVG" or "MATH" or "TEXTAREA" or "SELECT")
            {
                e.Tag.TextContent = string.Empty;
            }
        };
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "strike", "code", "pre", "blockquote", "ul", "ol", "li", "a", "span", "h1", "h2", "h3" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedClasses.Add("mention");
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }
}
