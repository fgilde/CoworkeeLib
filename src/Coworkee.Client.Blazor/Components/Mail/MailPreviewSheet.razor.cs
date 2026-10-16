using System.Net;
using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>A side sheet next to the template editor that follows every change: desktop or phone width, light or dark mail client, inbox line with the subject.</summary>
public sealed partial class MailPreviewSheet : IDisposable
{
    // ponytail: mail clients darken mails in their own ways; inverting light colours (images back) is close enough to spot unreadable text
    private const string DarkStyle = "<style>html{filter:invert(1) hue-rotate(180deg);background:#fff}img,picture,video,[style*=\"background-image\"]{filter:invert(1) hue-rotate(180deg)}</style>";

    private bool _phone;
    private bool _dark;
    private MailPreview? _subscribed;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public MailPreview Preview { get; set; } = null!;

    private string Initial => Preview.Rendered?.Subject is { Length: > 0 } subject ? subject[..1].ToUpperInvariant() : "@";

    protected override void OnParametersSet()
    {
        if (_subscribed != Preview)
        {
            Dispose();
            _subscribed = Preview;
            Preview.Changed += Refresh;
        }
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    private string Document(string html) => !_dark ? html
        : Head().Match(html) is { Success: true } head ? html.Insert(head.Index + head.Length, DarkStyle) : DarkStyle + html;

    private static string Preheader(string html)
    {
        var body = Body().Match(html) is { Success: true } match ? match.Groups[1].Value : html;
        var text = WebUtility.HtmlDecode(Whitespace().Replace(Tags().Replace(body, " "), " ")).Trim();
        return text.Length > 120 ? text[..120] + "…" : text;
    }

    public void Dispose()
    {
        if (_subscribed is not null)
        {
            _subscribed.Changed -= Refresh;
        }
    }

    [GeneratedRegex(@"<head(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex Head();

    [GeneratedRegex(@"<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Body();

    [GeneratedRegex(@"<(style|head)[^>]*>.*?</\1>|<[^>]+>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
