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
    /// <summary>The link that opts the user out of newsletters; absolute from the public app address, else from the current request, else relative.</summary>
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
        : string.Empty;
}

internal sealed class MailUnsubscribe(IMailUnsubscribeLinks links, IDispatcher dispatcher, CoworkeeDbContext db, IEnumerable<ITextTranslator> translators)
{
    public const string Text = "You will no longer receive newsletters.";

    public async Task<IResult> RunAsync(string? token, CancellationToken cancellationToken)
    {
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

        var culture = (await RecipientCultures.ForAsync(db, new Dictionary<Guid, Guid?> { [userId] = tenantId }, cancellationToken))[userId] ?? CultureInfo.GetCultureInfo("en");
        var text = WebUtility.HtmlEncode(translators.Translate(Text, culture));
        return Results.Content(
            $"<!doctype html><html lang=\"{culture.Name}\" dir=\"{(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr")}\"><head><meta charset=\"utf-8\">"
            + $"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{text}</title></head>"
            + $"<body style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;text-align:center;padding:64px 16px\"><p style=\"font-size:18px\">{text}</p></body></html>",
            "text/html; charset=utf-8");
    }
}
