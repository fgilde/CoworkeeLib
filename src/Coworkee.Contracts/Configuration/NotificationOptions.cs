namespace Coworkee.Contracts.Configuration;

public sealed class NotificationOptions
{
    public const string Section = "Coworkee:Notifications";

    /// <summary>Base of the links in digest mails (the web app); without it the links stay relative.</summary>
    public string? PublicAppUrl { get; set; }

    /// <summary>When the daily digest goes out (cron, UTC).</summary>
    [Cron]
    public string DigestCron { get; set; } = "0 6 * * *";

    /// <summary>Web Push to the devices users subscribed; off without a VAPID key pair.</summary>
    public PushOptions Push { get; set; } = new();
}

/// <summary>The VAPID key pair (base64url: the uncompressed P-256 public key and the private scalar) and the contact push services may reach.</summary>
public sealed class PushOptions
{
    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }

    /// <summary>mailto: or https: address of the operator; empty takes <see cref="NotificationOptions.PublicAppUrl"/>.</summary>
    public string? Subject { get; set; }
}
