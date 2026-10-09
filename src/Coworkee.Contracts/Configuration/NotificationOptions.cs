namespace Coworkee.Contracts.Configuration;

public sealed class NotificationOptions
{
    public const string Section = "Coworkee:Notifications";

    /// <summary>Base of the links in digest mails (the web app); without it the links stay relative.</summary>
    public string? PublicAppUrl { get; set; }

    /// <summary>When the daily digest goes out (cron, UTC).</summary>
    [Cron]
    public string DigestCron { get; set; } = "0 6 * * *";
}
