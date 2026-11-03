namespace Coworkee.Contracts.Mailing;

public static class MailProviders
{
    public const string Smtp = "smtp";
    public const string SendGrid = "sendGrid";
    public const string Mailgun = "mailgun";
    public const string Mailchimp = "mailchimp";

    public static IReadOnlyList<string> All { get; } = [Smtp, SendGrid, Mailgun, Mailchimp];
}
