namespace Coworkee.Contracts.Configuration;

/// <summary>The VAPID key pair (base64url: the uncompressed P-256 public key and the private scalar) and the contact push services may reach.</summary>
public sealed class PushOptions
{
    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }

    /// <summary>mailto: or https: address of the operator; empty takes <see cref="NotificationOptions.PublicAppUrl"/>.</summary>
    public string? Subject { get; set; }
}
