using System.Buffers.Text;
using System.Net;
using FluentValidation;

namespace Coworkee.Notifications.Push;

internal sealed class SavePushSubscriptionValidator : AbstractValidator<SavePushSubscription>
{
    public SavePushSubscriptionValidator()
    {
        // the server posts to this address: only a push service on the internet, never a host of the own network
        RuleFor(c => c.Subscription.Endpoint).NotEmpty().MaximumLength(2000).Must(IsPushService).WithMessage("Not the address of a push service.");
        RuleFor(c => c.Subscription.P256dh).Must(k => Decodes(k, 65)).WithMessage("Not a P-256 public key.");
        RuleFor(c => c.Subscription.Auth).Must(k => Decodes(k, 16)).WithMessage("Not a 16 byte secret.");
        RuleFor(c => c.Subscription.Device).MaximumLength(200);
    }

    internal static bool IsPushService(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && uri.HostNameType == UriHostNameType.Dns && !IPAddress.TryParse(uri.IdnHost, out _) && uri.IdnHost.Contains('.')
        && !uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !uri.IdnHost.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
        && !uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    private static bool Decodes(string? value, int length)
    {
        try
        {
            return value is { Length: > 0 and < 200 } && Base64Url.DecodeFromChars(value).Length == length;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
