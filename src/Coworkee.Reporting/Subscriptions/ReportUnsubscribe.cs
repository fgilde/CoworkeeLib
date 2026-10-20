using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Coworkee.Application.Localization;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>Links of subscription mails: back to the report and to end the subscription without signing in (a protected token names it).</summary>
internal sealed class ReportMailLinks(IDataProtectionProvider provider, IOptions<NotificationOptions> options)
{
    public const string Route = "/api/v1/reports/subscriptions/unsubscribe";

    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Reports.Unsubscribe");

    public string Report(Guid reportId) => $"{Root()}/reports/{reportId}";

    public string Unsubscribe(Guid subscriptionId, Guid tenantId) =>
        $"{Root()}{Route}?token={Uri.EscapeDataString(_protector.Protect($"{subscriptionId:N}:{tenantId:N}"))}";

    public bool TryRead(string? token, out Guid subscriptionId, out Guid tenantId)
    {
        subscriptionId = default;
        tenantId = default;
        try
        {
            var parts = string.IsNullOrEmpty(token) ? [] : _protector.Unprotect(token).Split(':');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out subscriptionId) && Guid.TryParseExact(parts[1], "N", out tenantId);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // mails are written by a job: there is no request to take the address from
    private string Root() => options.Value.PublicAppUrl is { Length: > 0 } root
        ? root.TrimEnd('/')
        : throw new InvalidOperationException("Set Coworkee:Notifications:PublicAppUrl so report mails can link back to the app.");
}

/// <summary>The confirm page of the unsubscribe link and its form post; opening the link changes nothing, mail scanners follow links.</summary>
internal sealed class ReportUnsubscribe(ReportMailLinks links, IServiceScopeFactory scopes, IEnumerable<ITextTranslator> translators)
{
    public const string Ask = "Do you want to stop getting this report by mail?";
    public const string Button = "End the subscription";
    public const string Done = "You will no longer get this report by mail.";

    public async Task<IResult> ConfirmAsync(HttpContext http, string? token, CancellationToken cancellationToken)
    {
        Harden(http);
        if (!links.TryRead(token, out var id, out var tenantId) || await FindAsync(id, tenantId, remove: false, cancellationToken) is not { } culture)
        {
            return Results.BadRequest();
        }

        var form = $"<p style=\"font-size:18px\">{Text(Ask, culture)}</p><form method=\"post\" action=\"{WebUtility.HtmlEncode(http.Request.PathBase + ReportMailLinks.Route)}\">"
            + $"<input type=\"hidden\" name=\"token\" value=\"{WebUtility.HtmlEncode(token)}\">"
            + $"<button type=\"submit\" style=\"font-size:16px;padding:10px 24px;cursor:pointer\">{Text(Button, culture)}</button></form>";
        return Page(culture, Text(Ask, culture), form);
    }

    public async Task<IResult> UnsubscribeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        Harden(http);
        string? token = null;
        try
        {
            token = http.Request.HasFormContentType ? (await http.Request.ReadFormAsync(cancellationToken))["token"].ToString() : null;
        }
        catch (InvalidDataException)
        {
            // a malformed form carries no token and ends in the 400 below
        }

        if (!links.TryRead(token, out var id, out var tenantId) || await FindAsync(id, tenantId, remove: true, cancellationToken) is not { } culture)
        {
            return Results.BadRequest();
        }

        return Page(culture, Text(Done, culture), $"<p style=\"font-size:18px\">{Text(Done, culture)}</p>");
    }

    /// <summary>The subscriber's language, null when the subscription is gone; with <paramref name="remove"/> it is deleted.</summary>
    private async Task<CultureInfo?> FindAsync(Guid id, Guid tenantId, bool remove, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        if (await db.Set<ReportSubscription>().FirstOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } subscription)
        {
            return null;
        }

        if (remove)
        {
            db.Remove(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }

        return (await RecipientCultures.ForAsync(db, new Dictionary<Guid, Guid?> { [subscription.UserId] = tenantId }, cancellationToken))[subscription.UserId] ?? CultureInfo.GetCultureInfo("en");
    }

    private static void Harden(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private string Text(string key, CultureInfo culture) => WebUtility.HtmlEncode(translators.Translate(key, culture));

    private static IResult Page(CultureInfo culture, string title, string body) => Results.Content(
        $"<!doctype html><html lang=\"{culture.Name}\" dir=\"{(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr")}\"><head><meta charset=\"utf-8\">"
        + $"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{title}</title></head>"
        + $"<body style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;text-align:center;padding:64px 16px\">{body}</body></html>",
        "text/html; charset=utf-8");
}
