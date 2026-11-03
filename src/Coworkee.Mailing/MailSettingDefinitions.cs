using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Mailing;

internal sealed class MailSettingDefinitions : ISettingDefinitionContributor
{
    private static readonly SettingScope[] Scopes = [SettingScope.Global, SettingScope.Tenant];

    private static SettingCondition Provider(params string[] providers) => new(MailSettings.Provider, providers);

    public void Define(SettingDefinitionContext context) =>
        context.Group("Mail", "Mail", page: "/admin/mail-settings")
            .Add(MailSettings.UseOwnServer, "Own mail server", SettingType.Bool, [SettingScope.Tenant], "false",
                description: "On: this organisation sends with the settings below. Off: mails go through the system's server.")
            .Add(MailSettings.Provider, "Provider", SettingType.Choice, Scopes, MailProviders.Smtp, choices: [.. MailProviders.All])
            .Add(MailSettings.Host, "SMTP host", SettingType.String, Scopes, enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.Port, "SMTP port", SettingType.Int, Scopes, "25", enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.UseSsl, "Use SSL", SettingType.Bool, Scopes, "false", enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.UserName, "SMTP user name", SettingType.String, Scopes, enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.Password, "SMTP password", SettingType.Secret, Scopes, enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.ApiKey, "API key", SettingType.Secret, Scopes,
                enabledWhen: Provider(MailProviders.SendGrid, MailProviders.Mailgun, MailProviders.Mailchimp))
            .Add(MailSettings.MailgunDomain, "Mailgun domain", SettingType.String, Scopes, enabledWhen: Provider(MailProviders.Mailgun))
            .Add(MailSettings.MailgunRegion, "Mailgun region", SettingType.Choice, Scopes, "us", choices: ["us", "eu"], enabledWhen: Provider(MailProviders.Mailgun))
            .Add(MailSettings.From, "Sender address", SettingType.String, Scopes, "noreply@localhost")
            .Add(MailSettings.Whitelist, "Recipient whitelist", SettingType.Text, Scopes,
                description: "Leave empty to allow all recipients. One address or @domain per line.")
            .Add(MailSettings.Newsletters, "Receive newsletters", SettingType.Bool, [SettingScope.Global, SettingScope.User], "true", visibleToClient: true);
}
