namespace Coworkee.Contracts.Mailing;

public static class MailSettings
{
    public const string Provider = "Mail.Provider";
    public const string Host = "Mail.Smtp.Host";
    public const string Port = "Mail.Smtp.Port";
    public const string UseSsl = "Mail.Smtp.UseSsl";
    public const string UserName = "Mail.Smtp.UserName";
    public const string Password = "Mail.Smtp.Password";
    public const string ApiKey = "Mail.ApiKey";
    public const string MailgunDomain = "Mail.Mailgun.Domain";
    public const string MailgunRegion = "Mail.Mailgun.Region";
    public const string From = "Mail.From";
    public const string Whitelist = "Mail.Whitelist";
    public const string UseOwnServer = "Mail.UseOwnServer";
    public const string Newsletters = "Mail.Newsletters";
}

public static class MailProviders
{
    public const string Smtp = "smtp";
    public const string SendGrid = "sendGrid";
    public const string Mailgun = "mailgun";
    public const string Mailchimp = "mailchimp";

    public static IReadOnlyList<string> All { get; } = [Smtp, SendGrid, Mailgun, Mailchimp];
}
