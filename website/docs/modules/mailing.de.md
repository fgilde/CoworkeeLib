# Mailversand

Mails sind Vorlagen mit einem Modell. Module definieren sie mit Standardtexten pro Sprache; Administratoren überschreiben Betreff und Text in der Oberfläche. Der Versand läuft über eine Warteschlange mit Wiederholungen in einem Hintergrundjob, jede Mail landet im Mail-Log.

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

Vorlagen nutzen [Scriban](https://github.com/scriban/scriban). Das Beispielmodell füllt die Vorschau und die Schaltfläche *Send test* im Vorlageneditor. Ein `en`-Standardtext ist Pflicht.

## Eigene Vorlagen

Neben den Vorlagen der Module legen Administratoren unter **Administration > Communication > Mail templates** (/admin/mail/templates, Berechtigung Mail.Templates.Manage) eigene an. **Neue Vorlage** fragt nach einem Namen (Buchstaben, Ziffern, ., _ und -; er lässt sich später nicht ändern und darf keine Vorlage des Systems oder der Organisation wiederholen), einem Anzeigenamen und einer Beschreibung; danach öffnet sich der Editor. Die Kopieren-Schaltfläche neben einer Vorlage legt aus ihrem Text in allen Sprachen eine eigene Vorlage an, unter einem freien Namen wie Identity.Welcome.copy. Eigene Vorlagen tragen die Markierung *custom*; nur sie lassen sich löschen, dabei entfallen auch ihre Texte. Benutzer der Systemorganisation können **Global** ankreuzen: Die Vorlage steht dann jeder Organisation zur Verfügung.
## Anbieter

Mails verlassen das System über genau einen *Anbieter*. `Mail.Provider` wählt ihn, die übrigen Einstellungen gehören zum Anbieter und stehen in derselben Gruppe. Jeder Anbieter braucht eine Absenderadresse (`Mail.From`); die optionale Whitelist (`Mail.Whitelist`, eine Adresse oder `@domain` je Zeile, leer erlaubt alle) gilt für alle.

| Anbieter | Wert | Einstellungen | Hinweise |
|---|---|---|---|
| SMTP | `smtp` (Standard) | `Mail.Smtp.Host` (Pflicht), `Mail.Smtp.Port` (25), `Mail.Smtp.UseSsl`, `Mail.Smtp.UserName`, `Mail.Smtp.Password` | die bisherigen Schlüssel, nichts zu migrieren; ohne SSL wechselt die Verbindung mit STARTTLS, wenn der Server es anbietet |
| SendGrid | `sendGrid` | `Mail.ApiKey` (Pflicht) | ein API-Schlüssel mit der Berechtigung *Mail Send* |
| Mailgun | `mailgun` | `Mail.ApiKey` und `Mail.Mailgun.Domain` (beide Pflicht), `Mail.Mailgun.Region` (`us` oder `eu`, Standard `us`) | die Region muss zu Ihrem Mailgun-Konto passen: EU-Konten nutzen `api.eu.mailgun.net` |
| Mailchimp Transactional | `mailchimp` | `Mail.ApiKey` (Pflicht) | der Schlüssel von **Mailchimp Transactional** (früher Mandrill, `mandrillapp.com`), nicht von Mailchimp Marketing: Es ist ein kostenpflichtiges Add-on eines Mailchimp-Kontos, das Sie unter *Transactional* anlegen und dessen Schlüssel *keine* Marketing-API-Schlüssel sind. Ein Marketing-Schlüssel wird abgelehnt |

Die HTTP-Anbieter sprechen ihre REST-API direkt über `IHttpClientFactory` an; es kommt kein zusätzliches Paket dazu. `Mail.Smtp.Password` und `Mail.ApiKey` sind Secrets: verschlüsselt gespeichert und nie an den Client gesendet. Lehnt ein Anbieter eine Mail ab, schlägt der Job fehl und wiederholt wie bei jedem anderen Fehler; die Meldung (mit maskiertem Schlüssel) steht im Mail-Log.

## Die Seite Mail-Einstellungen

**Administration > System > Mail settings** (`/admin/mail-settings`, Berechtigung `Settings.Manage`) ersetzt den Mail-Tab der Einstellungsseite. Sie wählen den Anbieter, füllen seine Felder, tragen Absender und Whitelist ein und speichern; Änderungen gelten ab der nächsten Mail, ohne Neustart. **Send test mail** schickt eine Mail an Ihre eigene Adresse mit den Werten, die gerade im Formular stehen, gespeichert oder nicht, und zeigt das Ergebnis: Ein leeres Secret-Feld behält den gespeicherten Schlüssel, Sie testen also, ohne ihn neu einzutippen. Bei einem Fehler steht die Antwort des Anbieters da; für Organisationen (siehe unten) nur eine kurze Meldung, denn eine Organisation wählt ihren Host selbst und darf die Netzwerkfehler des Servers nicht lesen.

## Eigener Server je Organisation

Organisationen versenden standardmäßig über den Server des Systems. Die Einstellung `Mail.UseOwnServer` (Geltungsbereich Organisation, standardmäßig aus) lässt eine Organisation ihren eigenen nutzen: Auf der Seite Mail-Einstellungen sieht der Administrator der Organisation den Schalter und, wenn er an ist, dasselbe Formular für Anbieter und Zugangsdaten.

- Ist der Schalter **an**, kommen alle Werte aus der Organisation: Anbieter, Zugangsdaten, Absender und Whitelist. Ist er **aus**, kommen alle aus dem System. Es wird nichts gemischt, eine Organisation sendet also nie mit dem Passwort des Systems und einem eigenen Absender.
- Ausschalten behält die Werte der Organisation; beim erneuten Einschalten gelten sie, ohne sie neu einzugeben.
- Die Werte, die der Anbieter braucht (SMTP-Host, API-Schlüssel, Mailgun-Domain), müssen in der Organisation selbst gespeichert sein. Ein Standardwert des Deployments wie `Coworkee:Settings:Defaults:Mail.Smtp.Host` zählt nicht, sonst würde eine Organisation über den Server des Systems senden, während sie glaubt, ihren eigenen zu nutzen; die Mail scheitert stattdessen mit *... is not configured.*
- Die System-Organisation nutzt immer die Einstellungen des Systems.

## Versand und eigene Transporte

`IMailSender.QueueAsync` ist unverändert. Ein Hintergrundjob nimmt die eingereihte Mail, ermittelt die Verbindung (Anbieter und Werte) für die Organisation der Mail und übergibt sie dem `IMailTransport`, dessen `Provider` passt.

!!! warning "Breaking Change"
    `ISmtpTransport` entfällt und wird durch `IMailTransport` ersetzt, eine Implementierung je Anbieter. Es gibt keinen Obsolete-Shim. Die Schlüssel `Mail.Smtp.*`, `Mail.From` und `Mail.Whitelist` bleiben unverändert, gespeicherte Werte gelten also weiter. Code, der `ISmtpTransport` implementiert oder ersetzt hat, implementiert stattdessen `IMailTransport`.

```csharp
public interface IMailTransport
{
    string Provider { get; }

    Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken);
}
```

`MailEnvelope` enthält `From`, `To`, `Subject` und `HtmlBody`; `MailConnection` den Namen des Anbieters und die ermittelten Werte nach Einstellungsname (`connection[MailSettings.ApiKey]`, null, wenn leer). Eine Ausnahme lässt die Mail scheitern, der Job wiederholt sie. Ein Transport mit eigenem Namen ist ein neuer Anbieter. Dieser schreibt Mails ins Log, was in Tests praktisch ist:

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

Die Einstellungsseite bietet nur die vier eingebauten Anbieter an. Den eigenen wählen Sie über seinen Namen als Standardwert: `Coworkee:Settings:Defaults:Mail.Provider` = `log`. Seine Zugangsdaten, falls er welche braucht, kommen aus Ihren eigenen Einstellungen oder der Konfiguration; für einen unbekannten Anbieter prüft das System keine Pflichtwerte. Genauso geht es mit einem Dienst mit HTTP-API wie Amazon SES: `SendAsync` mit einem `HttpClient` aus `IHttpClientFactory` implementieren und die Anfrage so signieren, wie der Dienst es verlangt.

Lokal zeigt der AppHost die SMTP-Einstellungen auf Mailpit.