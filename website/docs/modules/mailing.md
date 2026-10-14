# Mailing

Mails are templates with a model. Modules define them with defaults per culture; administrators can override subject and body in the UI. Sending is queued and retried by a background job, every mail lands in the mail log.

```csharp
internal sealed class OrderMailTemplates : IMailTemplateContributor
{
    public void Define(MailTemplateContext context) =>
        context.Add(new MailTemplateDefinition("Orders.Shipped", "Order shipped", new OrderShippedMail("Ada", "A-1001"),
            new Dictionary<string, MailTemplateContent>
            {
                ["en"] = new("Your order {{ number }} is on its way", "Hello {{ name }}, ..."),
                ["de"] = new("Ihre Bestellung {{ number }} ist unterwegs", "Hallo {{ name }}, ..."),
            }));
}

await mails.QueueAsync(customer.Email, "Orders.Shipped", new OrderShippedMail(customer.Name, order.Number), culture: "de", ct);
```

Templates use [Scriban](https://github.com/scriban/scriban). The sample model fills the preview and the *Send test* button of the template editor. An `en` default is required.

## Own templates

Besides the templates of the modules, administrators create their own under **Administration > Communication > Mail templates** (/admin/mail/templates, permission Mail.Templates.Manage). **New template** asks for a name (letters, digits, ., _ and -; it cannot change later and cannot repeat a template of the system or of the organisation), a display name and a description; the editor opens right after. The copy button next to a template creates a custom template from its text in all languages, under a free name like Identity.Welcome.copy. Own templates are marked *custom*; only they can be deleted, which also removes their texts. Users of the system organisation can tick **Global**: the template is then available to every organisation.
## Providers

Mails leave through one *provider*. `Mail.Provider` picks it, the other settings belong to the provider and sit in the same group. Every provider needs a sender address (`Mail.From`); the optional whitelist (`Mail.Whitelist`, one address or `@domain` per line, empty allows everyone) applies to all of them.

| Provider | Value | Settings | Notes |
|---|---|---|---|
| SMTP | `smtp` (default) | `Mail.Smtp.Host` (required), `Mail.Smtp.Port` (25), `Mail.Smtp.UseSsl`, `Mail.Smtp.UserName`, `Mail.Smtp.Password` | the existing keys, nothing to migrate; without SSL the connection upgrades with STARTTLS when the server offers it |
| SendGrid | `sendGrid` | `Mail.ApiKey` (required) | an API key with the *Mail Send* permission |
| Mailgun | `mailgun` | `Mail.ApiKey` and `Mail.Mailgun.Domain` (both required), `Mail.Mailgun.Region` (`us` or `eu`, default `us`) | the region must match the one of your Mailgun account: EU accounts use `api.eu.mailgun.net` |
| Mailchimp Transactional | `mailchimp` | `Mail.ApiKey` (required) | the key of **Mailchimp Transactional** (formerly Mandrill, `mandrillapp.com`), not of Mailchimp marketing: it is a paid add-on of a Mailchimp account that you create under *Transactional* and whose keys are *not* the marketing API keys. A marketing key is rejected |

The HTTP providers talk to their REST API directly with `IHttpClientFactory`; no extra package. `Mail.Smtp.Password` and `Mail.ApiKey` are secrets: encrypted at rest and never sent to the client. A provider that rejects a mail makes the job fail and retry like any other error; the message (with the key masked) is in the mail log.

## The mail settings page

**Administration > System > Mail settings** (`/admin/mail-settings`, permission `Settings.Manage`) replaces the Mail tab of the settings page. You choose the provider, fill its fields, set sender and whitelist and save; changes apply to the next mail, without a restart. **Send test mail** sends a mail to your own address with the values currently in the form, saved or not, and shows the result: an empty secret field keeps the stored key, so you can test without typing it again. A failure shows the provider's answer; for organisations (see below) it shows only a short message, because an organisation chooses its own host and must not read the network errors of the server.

## Own server per organisation

Organisations send through the system's server by default. The setting `Mail.UseOwnServer` (scope organisation, off by default) lets an organisation use its own: on the mail settings page an organisation administrator sees the switch, and with it on the same form for provider and credentials.

- With the switch **on**, every value comes from the organisation: provider, credentials, sender and whitelist. With it **off**, every value comes from the system. Nothing is mixed, so an organisation never sends with the system's password and a sender of its own.
- Switching off keeps the organisation's values; switching on again uses them without typing them in.
- The values the provider needs (SMTP host, API key, Mailgun domain) must be stored in the organisation itself. A deployment default such as `Coworkee:Settings:Defaults:Mail.Smtp.Host` does not count, otherwise an organisation would send through the system's server while believing it uses its own; the mail fails with *... is not configured.* instead.
- The system organisation always uses the system settings.

## Sending and own transports

`IMailSender.QueueAsync` is unchanged. A background job takes the queued mail, resolves the connection (provider and values) for the mail's organisation and hands it to the `IMailTransport` whose `Provider` matches.

!!! warning "Breaking change"
    `ISmtpTransport` is gone, replaced by `IMailTransport`, one implementation per provider. There is no obsolete shim. The settings keys `Mail.Smtp.*`, `Mail.From` and `Mail.Whitelist` are unchanged, so stored values keep working. Code that implemented or replaced `ISmtpTransport` implements `IMailTransport` instead.

```csharp
public interface IMailTransport
{
    string Provider { get; }

    Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken);
}
```

`MailEnvelope` holds `From`, `To`, `Subject` and `HtmlBody`; `MailConnection` the provider name and the resolved values by setting name (`connection[MailSettings.ApiKey]`, null when empty). Throw to fail the mail; the job retries it. A transport with a name of its own is a new provider. This one writes mails to the log, which is handy in tests:

```csharp
using Coworkee.Mailing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

public sealed class LogMailTransport(ILogger<LogMailTransport> logger) : IMailTransport
{
    public string Provider => "log";

    public Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken)
    {
        logger.LogInformation("Mail from {From} to {To}: {Subject}", mail.From, mail.To, mail.Subject);
        return Task.CompletedTask;
    }
}

// in ConfigureServices
services.TryAddEnumerable(ServiceDescriptor.Singleton<IMailTransport, LogMailTransport>());
```

The settings page offers the four built-in providers only. Select your own by its name as the default: `Coworkee:Settings:Defaults:Mail.Provider` = `log`. Its credentials, if it needs any, come from your own settings or configuration; the system checks no required values for a provider it does not know. The same works for a service with an HTTP API such as Amazon SES: implement `SendAsync` with an `HttpClient` from `IHttpClientFactory` and sign the request the way the service demands.

Locally the app host points the SMTP settings to Mailpit.