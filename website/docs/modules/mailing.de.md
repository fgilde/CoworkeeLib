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

### Vorlage bearbeiten

Der Inhalt hat zwei Modi, die über dem Editor umgeschaltet werden. **Visuell** ist ein Rich-Text-Editor: Mit `{` öffnet sich die Liste der Platzhalter (sie filtert beim Tippen, auch für `user.first`), der gewählte erscheint als Chip. **Quelltext** ist ein Code-Editor für das HTML und den Scriban-Text, mit Vorschlägen nach `{{`. Gespeichert wird immer reiner Scriban-Text, ein Chip wird als `{{ user.first_name }}` abgelegt.

Der visuelle Editor schreibt das Markup um und wird deshalb nur für einfache Inhalte angeboten. Ein Inhalt mit Scriban-Anweisungen (`if`, `for`, `while`, `case`, `capture`, `with`, `func`, `tablerow`) oder einem `<table` öffnet sich im Quelltext, die Schaltfläche Visuell bleibt deaktiviert; von Visuell nach Quelltext lässt sich immer wechseln. Platzhalter, die die Vorlage nicht kennt, bleiben im visuellen Editor einfacher Text.

Neben dem Editor listet **Platzhalter** alle Variablen der Vorlage mit Typ und Beispielwert. Ein Klick fügt sie an der Cursorposition ein: als Chip im Visuell-Modus, als `{{ name }}` im Quelltext. Eine Liste (etwa die Benachrichtigungen eines Digests) fügt im Quelltext ein Schleifengerüst `{{ for item in liste }} ... {{ end }}` ein; ihre Mitglieder (aufgelistet als liste[].title) werden als {{ item.title }} eingefügt. Im Visuell-Modus kommen beide ans Ende des Quelltexts, weil sich Schleifen nicht visuell bauen lassen. Die Schaltfläche neben dem Betreff hängt einen Platzhalter an den Betreff an. Eigene Vorlagen verwenden das Empfängermodell (`user.first_name`, `user.display_name`, `app.name` und die weiteren Werte der Liste); *Auf Standard zurücksetzen* gibt es nur für Vorlagen der Module, eigene Vorlagen haben keinen Standard.

## Kampagnen

**Administration > Communication > Mail campaigns** (/admin/mail/campaigns, Berechtigung Mail.Campaigns.Send) versendet eine eigene Vorlage an viele Benutzer Ihrer Organisation. Eine Kampagne erreicht immer nur Benutzer der Organisation dessen, der sie versendet, auch bei der Systemorganisation. Eingebaute Vorlagen lassen sich nicht versenden, sie brauchen ein eigenes Modell; kopieren Sie eine, um zuerst eine eigene Vorlage zu erhalten.

**Neue Kampagne** öffnet einen Assistenten mit drei Schritten:

1. **Vorlage**: eine eigene Vorlage wählen und die Kampagne benennen.
2. **Empfänger**: festlegen, wer sie erhält, siehe unten. Das Feld unter der Auswahl zeigt immer, wie viele Benutzer das sind, wie viele ausgeschlossen wurden und die ersten Empfänger.
3. **Senden**: Zusammenfassung, **Testmail an mich senden** (schickt die Vorlage an Ihre eigene Adresse, ihre Fußzeile verlinkt Ihre echte Abmeldeseite), **Jetzt senden** oder **Planen** mit Datum und Uhrzeit. Beides fragt noch einmal mit der Anzahl der Empfänger nach. Die Zeit ist die Uhrzeit Ihres Browsers; eine Zeit, die mehr als eine Minute in der Vergangenheit liegt, wird abgelehnt.

Empfänger stammen aus einer dieser Quellen:

- **Intelligente Liste**: eine Voreinstellung (alle aktiven Benutzer; seit mehr als N Tagen nicht angemeldet, Standard 30; noch nie angemeldet; in den letzten N Tagen registriert; E-Mail nicht bestätigt; Mitglieder von Rollen; deaktivierte Benutzer), eine **gespeicherte Liste** oder eine **eigene Abfrage**, gebaut mit demselben Abfrage-Editor wie der erweiterte Filter der Benutzertabelle. **Als Liste speichern** legt eine Abfrage unter einem Namen für spätere Kampagnen ab; eine gespeicherte Liste wird in die Kampagne kopiert, spätere Änderungen oder das Löschen der Liste ändern eine bereits geplante Kampagne also nicht.
- **Manuelle Auswahl**: die Benutzertabelle mit Kontrollkästchen. Suche, Facetten und der erweiterte Filter grenzen sie vorab ein. **Alle Treffer auswählen** wechselt von den angekreuzten Zeilen zu allen Benutzern, die dem aktuellen Filter entsprechen, auch auf anderen Seiten.

Eine eigene Abfrage und eine manuelle Auswahl brauchen zusätzlich das Recht, Benutzer anzuzeigen (`Identity.Users.View`), weil sie Personen nach Namen auswählen lassen. Mit nur `Mail.Campaigns.Send` funktionieren die Voreinstellungen, die Vorschau zeigt dann die Zahlen ohne die Namen.

Benutzer ohne E-Mail-Adresse, gesperrte Benutzer und Benutzer, die **Newsletter erhalten** abgeschaltet haben, werden nie angeschrieben; deaktivierte Benutzer nur mit der Voreinstellung *Deaktivierte Benutzer*. Sie werden als ausgeschlossen gezählt. Die Empfänger werden beim Start der Kampagne aufgelöst, nicht bei der Auswahl; eine geplante Kampagne erreicht also die Benutzer dieses Zeitpunkts. Jede Mail wird in der Sprache des Empfängers erzeugt: der des Benutzers, sonst der der Organisation, sonst der Standardsprache.

Jede Kampagnenmail endet mit einem Abmeldelink. Er öffnet ohne Anmeldung eine Bestätigungsseite; die Bestätigung schaltet die Einstellung **Newsletter erhalten** (`Mail.Newsletters`, standardmäßig an) für diesen Benutzer ab. Benutzer können sie in ihren eigenen Einstellungen wieder einschalten. Der Link trägt ein signiertes Token und läuft nicht ab; das Token wird mit dem gemeinsamen Data-Protection-Schlüsselring in der Datenbank geschützt, die Links funktionieren also über Neustarts und Hosts hinweg.

Die Liste zeigt jede Kampagne mit Status, `Gesendet/Gesamt` und einer roten Zahl *fehlgeschlagen*, wenn Mails nicht zugestellt werden konnten; sie aktualisiert sich alle fünf Sekunden, solange eine Kampagne sendet. Die Log-Schaltfläche öffnet das Mail-Log gefiltert auf diese Kampagne (`/admin/mail/log?campaign=<id>`). **Kampagne abbrechen** gibt es für geplante und sendende Kampagnen: Es werden keine weiteren Empfänger eingereiht, noch wartende Mails werden aus der Warteschlange entfernt; bereits gesendete bleiben gesendet. Andere Kampagnen lassen sich löschen.

!!! warning
    Links in Kampagnenmails brauchen eine absolute Adresse. Setzen Sie `Coworkee:Notifications:PublicAppUrl` (zum Beispiel `https://app.example.com`); ohne sie lässt sich keine Kampagne starten oder planen. Das Abmelde-Token ist mit ASP.NET Core Data Protection geschützt: Läuft der Kampagnen-Job in einem anderen Host als die Web-App, müssen beide denselben Data-Protection-Schlüsselring teilen, sonst öffnen sich die Links nicht.

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

Ein Hintergrundjob nimmt die eingereihte Mail, ermittelt die Verbindung (Anbieter und Werte) für die Organisation der Mail und übergibt sie dem `IMailTransport`, dessen `Provider` passt.

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