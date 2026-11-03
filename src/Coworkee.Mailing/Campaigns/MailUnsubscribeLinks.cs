using System.Security.Cryptography;
using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Coworkee.Mailing;

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
