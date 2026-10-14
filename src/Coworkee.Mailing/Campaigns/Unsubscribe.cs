using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Coworkee.Application.Localization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

public interface IMailUnsubscribeLinks
{
    /// <summary>The link that opts the user out of newsletters; absolute from the public app address, else from the current request; throws when neither exists.</summary>
    string Create(Guid userId, Guid? tenantId);

    bool TryRead(string? token, out Guid userId, out Guid? tenantId);
}

internal sealed class MailUnsubscribeLinks(IDataProtectionProvider provider, IOptions<NotificationOptions> options, IHttpContextAccessor http) : IMailUnsubscribeLinks
{
    public const string Route = "/api/v1/mail/unsubscribe";

    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Mail.Unsubscribe");

    public string Create(Guid userId, Guid? tenantId) =>
        $"{Root()}{Route}?token={Uri.EscapeDataString(_protector.Protect($"{userId:N}:{tenantId:N}"))}";

    public bool TryRead(string? token, out Guid userId, out Guid? tenantId)
    {
        userId = default;
        tenantId = null;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            var parts = _protector.Unprotect(token).Split(':');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out userId))
            {
                return false;
            }

            if (parts[1].Length > 0)
            {
                if (!Guid.TryParseExact(parts[1], "N", out var tenant))
                {
                    return false;
                }

                tenantId = tenant;
            }

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private string Root() =>
        options.Value.PublicAppUrl is { Length: > 0 } root ? root.TrimEnd('/')
        : http.HttpContext?.Request is { } request ? $"{request.Scheme}://{request.Host}{request.PathBase}"
        : throw new InvalidOperationException("Set Coworkee:Notifications:PublicAppUrl so mails can link back to the app.");
}

internal sealed class MailUnsubscribe(IMailUnsubscribeLinks links, IDispatcher dispatcher, CoworkeeDbContext db, IEnumerable<ITextTranslator> translators)
{
    public const string Done = "You will no longer receive newsletters.";
    public const string Ask = "Do you want to stop receiving newsletters?";
    public const string Button = "Unsubscribe from newsletters";

    /// <summary>The confirm page; opening the link changes nothing, mail scanners follow links.</summary>
    public async Task<IResult> ConfirmAsync(HttpContext http, string? token, CancellationToken cancellationToken)
    {
        Harden(http);
        if (!links.TryRead(token, out var userId, out var tenantId))
        {
            return Results.BadRequest();
        }

        var culture = await CultureAsync(userId, tenantId, cancellationToken);
        var form = $"<p style=\"font-size:18px\">{Text(Ask, culture)}</p><form method=\"post\" action=\"{MailUnsubscribeLinks.Route}\">"
            + $"<input type=\"hidden\" name=\"token\" value=\"{WebUtility.HtmlEncode(token)}\">"
            + $"<button type=\"submit\" style=\"font-size:16px;padding:10px 24px;cursor:pointer\">{Text(Button, culture)}</button></form>";
        return Page(culture, Text(Ask, culture), form);
    }

    public async Task<IResult> UnsubscribeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        Harden(http);
        var token = http.Request.HasFormContentType ? (await http.Request.ReadFormAsync(cancellationToken))["token"].ToString() : null;
        if (!links.TryRead(token, out var userId, out var tenantId))
        {
            return Results.BadRequest();
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        var saved = await dispatcher.SendAsync(new SetUserSettings(new Dictionary<string, string?> { [MailSettings.Newsletters] = "false" }), cancellationToken);
        if (!saved.IsSuccess)
        {
            return Results.Problem(saved.Error!.Message);
        }

        var culture = await CultureAsync(userId, tenantId, cancellationToken);
        return Page(culture, Text(Done, culture), $"<p style=\"font-size:18px\">{Text(Done, culture)}</p>");
    }

    private static void Harden(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private async Task<CultureInfo> CultureAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken) =>
        (await RecipientCultures.ForAsync(db, new Dictionary<Guid, Guid?> { [userId] = tenantId }, cancellationToken))[userId] ?? CultureInfo.GetCultureInfo("en");

    private string Text(string key, CultureInfo culture) => WebUtility.HtmlEncode(translators.Translate(key, culture));

    private static IResult Page(CultureInfo culture, string title, string body) => Results.Content(
        $"<!doctype html><html lang=\"{culture.Name}\" dir=\"{(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr")}\"><head><meta charset=\"utf-8\">"
        + $"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{title}</title></head>"
        + $"<body style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;text-align:center;padding:64px 16px\">{body}</body></html>",
        "text/html; charset=utf-8");
}