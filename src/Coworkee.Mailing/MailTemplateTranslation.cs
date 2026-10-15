using System.Globalization;
using System.Text.RegularExpressions;
using Coworkee.Application.Localization;

namespace Coworkee.Mailing;

/// <summary>
/// Machine translates a mail template: only the texts between the HTML tags go to the service, each Scriban tag in them
/// as a {0} placeholder, so markup, styles and variables stay as they are.
/// </summary>
public static partial class MailTemplateTranslation
{
    /// <returns>The translated content, or null when nothing was translated (no service, or it answered nothing).</returns>
    public static async Task<MailTemplateContent?> TranslateAsync(MailTemplateContent source, string from, string to, IMachineTranslator translator, CancellationToken cancellationToken)
    {
        var subject = Split(source.Subject);
        var body = Split(source.Body);
        var texts = subject.Concat(body).Where(p => p.IsText).Select(p => p.Masked).Distinct(StringComparer.Ordinal).ToList();
        var translations = texts.Count == 0 ? new Dictionary<string, string>() : await translator.TranslateAsync(texts, from, to, cancellationToken);
        return translations.Count == 0 ? null : new MailTemplateContent(Join(subject, translations), Join(body, translations));
    }

    internal static List<Part> Split(string text)
    {
        var parts = new List<Part>();
        var start = 0;
        foreach (Match markup in Markup().Matches(text))
        {
            if (markup.Value.StartsWith("{{", StringComparison.Ordinal))
            {
                continue;
            }

            parts.Add(Part.Of(text[start..markup.Index]));
            parts.Add(Part.Tag(markup.Value));
            start = markup.Index + markup.Length;
        }

        parts.Add(Part.Of(text[start..]));
        return parts;
    }

    private static string Join(List<Part> parts, IReadOnlyDictionary<string, string> translations) =>
        string.Concat(parts.Select(p => p.IsText && translations.TryGetValue(p.Masked, out var translated) ? p.Restore(translated) : p.Raw));

    // a Scriban tag is matched first, so the "<" or ">" inside one never starts a HTML tag
    [GeneratedRegex(@"\{\{.*?\}\}|<(style|script)\b.*?</\1\s*>|<!--.*?-->|<[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Markup();

    [GeneratedRegex(@"\{\{.*?\}\}", RegexOptions.Singleline)]
    private static partial Regex Scriban();

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Index();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex Letter();

    internal sealed record Part(string Raw, string Masked, string[] Blocks, string Lead, string Trail, bool IsText)
    {
        public static Part Tag(string raw) => new(raw, string.Empty, [], string.Empty, string.Empty, false);

        public static Part Of(string raw)
        {
            var blocks = Scriban().Matches(raw).Select(m => m.Value).ToArray();
            var index = 0;
            var masked = Scriban().Replace(raw, _ => $"{{{index++}}}").Trim();
            var lead = raw[..(raw.Length - raw.TrimStart().Length)];
            var trail = raw[raw.TrimEnd().Length..];
            return new Part(raw, masked, blocks, lead, trail, Letter().IsMatch(Scriban().Replace(raw, string.Empty)));
        }

        public string Restore(string translated) =>
            Lead + Index().Replace(translated.Trim(), m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var i && i < Blocks.Length ? Blocks[i] : m.Value) + Trail;
    }
}
