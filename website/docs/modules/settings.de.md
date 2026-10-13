# Einstellungen und App-Konfiguration

Coworkee kennt zwei Arten von Konfiguration, die Administratoren zur Laufzeit ändern.

| | Einstellungen | App-Konfiguration |
|---|---|---|
| Für | einzelne Werte pro Installation, Mandant oder Benutzer (SMTP-Host, Theme, Registrierung an/aus) | eine typisierte Options-Klasse der App (Grenzen, Schalter, Integrationen) |
| Definiert durch | `ISettingDefinitionContributor` | eine C#-Klasse, optional aus JSON erzeugt |
| Gespeichert in | Einstellungstabelle, Geheimnisse verschlüsselt | `cw.ConfigurationEntries`, gelesen über `IConfiguration` |
| Gelesen mit | `ISettingProvider` | `IOptionsMonitor<T>` |
| Admin-Seite | Einstellungen, Tabs System und Organisation | Einstellungen, ein Tab pro Klasse (nur System-Mandant) |

## Einstellungen

```csharp
internal sealed class MailSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Mail", "Mail")
            .Add("Mail.Smtp.Host", "SMTP host", SettingType.String, [SettingScope.Global])
            .Add("Mail.Smtp.Port", "SMTP port", SettingType.Int, [SettingScope.Global], defaultValue: "25")
            .Add("Mail.Smtp.Password", "SMTP password", SettingType.Secret, [SettingScope.Global]);
}
```

Typen sind `String`, `Text`, `Bool`, `Int`, `Secret`, `Choice` und `Language`; Gültigkeitsbereiche `Global`, `Tenant` und `User`. Der spezifischste Wert gewinnt. Geheimnisse werden mit ASP.NET Core Data Protection verschlüsselt und nie an den Client geschickt; Einstellungen mit `visibleToClient: true` schon.

```csharp
var allowed = await settings.GetAsync<bool>(AccountSettings.AllowRegistration, ct);
```

Standardwerte können auch aus der Konfiguration kommen: `Coworkee:Settings:Defaults:Mail.Smtp.Host`. Der AppHost nutzt das für Mailpit.

Der Abschnitt **Cloud-Dateiauswahl** im Reiter System enthält die IDs von Google Drive, OneDrive und Dropbox für jedes Upload-Feld, siehe [Dateien](files.md#cloud-dateiauswahl).

### Eigene Einstellungen auf der Admin-Seite

Die Tabs **System** (Bereich `Global`) und **Organisation** (Bereich `Tenant`) sowie **Meine Einstellungen** des Benutzers (Bereich `User`) haben keinen Formularcode pro Einstellung. Sie laden die Definitionen und zeigen sie mit `MudExObjectEditForm`; eine Einstellung Ihres Moduls erscheint dort, sobald ihr Contributor registriert ist:

```csharp title="Server-Modul"
internal sealed class BillingSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Billing", "Billing")
            .Add("Billing.Currency", "Currency", SettingType.Choice, [SettingScope.Tenant], "EUR", choices: ["EUR", "USD", "CHF"],
                description: "The currency of new invoices.")
            .Add("Billing.ApiKey", "API key", SettingType.Secret, [SettingScope.Global]);
}

context.Services.AddSingleton<ISettingDefinitionContributor, BillingSettingDefinitions>();
```

| Definition | Formular |
|---|---|
| Gruppe | ein Abschnitt mit dem Anzeigenamen der Gruppe |
| Anzeigename, `description` | Beschriftung und Hilfetext (über die Übersetzungen, `de.json` kann sie also übersetzen) |
| `String` / `Int` | Text- oder Zahlenfeld; der Standardwert ist der Platzhalter, ein leeres Feld fällt auf den Standard zurück |
| `Text` | ein Textfeld mit vier Zeilen |
| `Bool` | ein Kontrollkästchen mit dem gespeicherten Wert oder dem Standard |
| `Choice` | eine Auswahl der `choices` |
| `Language` | eine Auswahl der Sprachen der App; leer heißt erben, angezeigt als *Configured default* im Tab System und *Inherited* in Organisation und Meine Einstellungen (dort beschriftet mit *Language*) |
| `Secret` | ein Passwortfeld, das leer bleibt; der Hilfetext sagt, ob ein Wert gespeichert ist, und nur ein eingetippter Wert wird gesendet |
| `enabledWhen` | das Feld ist deaktiviert, solange der Wert der anderen Einstellung nicht in den Werten der Bedingung liegt |
| Gruppe mit `page` | erscheint nicht in den Einstellungs-Tabs; siehe unten |
| Bereiche | eine Einstellung erscheint im Tab jedes ihrer Bereiche |

Speichern sendet nur geänderte Werte; der Server prüft sie (`Expected a whole number.` usw.), die Meldung erscheint in der Snackbar. Wer die Tabs sieht, folgt der Berechtigung `Settings.Manage`; **System** erscheint nur in der System-Organisation.

Hinter dem Formular steht eine zur Laufzeit erzeugte Klasse mit einer Eigenschaft je Einstellung (`Mail.Smtp.Host` wird `Mail_Smtp_Host`), das Formular verhält sich also wie jedes andere Objektformular. Felder haben kein eigenes Zurücksetzen: Ein geleertes Text- oder Zahlenfeld kehrt zum Standard zurück.

### Abhängige Einstellungen

`enabledWhen` bindet ein Feld an eine andere Einstellung: Es ist deaktiviert, solange deren Wert nicht unter den genannten Werten ist. `SettingCondition.IsTrue` deckt den häufigen Fall eines Schalters ab:

```csharp
context.Group("FilePickers", "Cloud file pickers")
    .Add("FilePickers.GoogleDrive.Enabled", "Google Drive", SettingType.Bool, [SettingScope.Global])
    .Add("FilePickers.GoogleDrive.ClientId", "Google Drive client id", SettingType.String, [SettingScope.Global],
        enabledWhen: SettingCondition.IsTrue("FilePickers.GoogleDrive.Enabled"));

// mehrere Werte: die Übersetzungsfelder hängen vom Anbieter ab
enabledWhen: new SettingCondition(LocalizationSettings.TranslationProvider, [TranslationProviders.DeepL, TranslationProviders.Azure])
```

Das ist nur eine Erleichterung im Formular; der Server erzwingt sie nicht. Eine Bedingung auf eine Einstellung, die nicht im Formular steht (etwa in einem anderen Bereich), lässt das Feld aktiv.

### Gruppen auf eigener Seite

`context.Group(name, displayName, page: "/admin/ai-settings")` markiert eine Gruppe als auf einer eigenen Admin-Seite gepflegt. Die Einstellungs-Tabs überspringen sie, **Meine Einstellungen** des Benutzers zeigen weiter jede Gruppe ohne `page`. So arbeitet die KI-Gruppe: Ihre Einstellungen, auch der MCP-Schalter, liegen auf `/admin/ai-settings`.

### Spracheinstellungen

`SettingType.Language` passt für Einstellungen, die eine Kultur halten. `Localization.Culture` (**Default language**) ist die Sprache, in der die App startet: Eine Organisation überschreibt den Systemwert, ein Benutzer überschreibt beide. Ein leerer Wert erbt.

![Einstellungsseite](../assets/screenshots/settings.png){ .shot }

## Typisierte App-Einstellungen

Die App beschreibt ihre Einstellungen als Klassen. Werte kommen aus appsettings und der Umgebung; was Administratoren ändern, liegt in `cw.ConfigurationEntries` und gilt darüber für jeden Dienst der App. Die Einstellungsseite zeigt jede Klasse mit `MudExObjectEditForm`, wie die klassische Einstellungsseite, und Dienste lesen `IOptionsMonitor<T>` und sehen Änderungen nach wenigen Sekunden. Werte, die nur beim Start gelesen werden, gelten nach einem Neustart.

Jeder Dienst, der die Änderungen sehen soll, nimmt die Datenbank-Schicht in seine Konfiguration auf:

```csharp title="Program.cs von API, Auth-Server und Workern"
builder.Configuration.AddCoworkeeDatabaseConfiguration("myapp"); // Name des Connection Strings der App-Datenbank
```

### Eingebaute App-Einstellungen

Ohne weiteren Code zeigt die Seite **App-Einstellungen**: `CoworkeeAppSettings`, gebunden an den Abschnitt `Coworkee`, mit Selbstregistrierung (`Registration`), Benachrichtigungs-Zusammenfassungen (`Notifications`) und Hintergrundjobs (`Jobs`). Verdrahtungswerte, die der AppHost setzt (`Jobs:ConnectionStringName`, `Jobs:RunServer`, `Jobs:DashboardPath`, `Notifications:PublicAppUrl`), sind gesperrt; Listen, an die der Configuration Binder nur anhängen könnte (`Jobs:Queues`, `Jobs:RetryDelaysInSeconds`), sind ausgeblendet.

### App-Einstellungen erweitern

Leiten Sie von `CoworkeeAppSettings` ab und registrieren Sie den Typ auf beiden Seiten. Die Sperren der eingebauten Abschnitte bleiben; eigene kommen hinzu:

```csharp title="Contracts"
public sealed class MyAppSettings : CoworkeeAppSettings
{
    public BillingSettings Billing { get; set; } = new();
}

public sealed class BillingSettings
{
    public string Currency { get; set; } = "EUR";
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
}
```

```csharp title="Server-Modul"
context.Services.AddCoworkeeSettings<MyAppSettings>(context.Configuration, rules => rules
    .Lock(s => s.Billing.Currency)          // sichtbar, nie geändert
    .Hide(s => s.Billing.WebhookSecret));   // verlässt den Server nie
```

```csharp title="Blazor-Client"
builder.Services.AddCoworkeeSettings<MyAppSettings>(meta =>
{
    meta.Property(s => s.Billing.ApiKey).WithGroup("Billing");
    meta.Property(s => s.Billing.Currency).WithGroup("Billing");
});
```

Dienste lesen `IOptionsMonitor<MyAppSettings>`; die neuen Eigenschaften liegen unter `Coworkee:Billing`.

### App-Einstellungen ersetzen

Jede andere Klasse ersetzt die eingebaute vollständig, meist mit eigenem Abschnitt:

```csharp
context.Services.AddCoworkeeSettings<ShopSettings>(context.Configuration, section: "Shop", title: "Shop");   // Server
builder.Services.AddCoworkeeSettings<ShopSettings>(section: "Shop", title: "Shop");                         // Client
```

### Regeln

| | |
|---|---|
| `Lock(s => s.A.B)` | der Wert ist nur lesbar; der Server behält, was gilt, egal was kommt. Ein gesperrtes Objekt sperrt alle seine Eigenschaften |
| `Hide(s => s.A.B)` | der Wert wird weder an den Browser geschickt noch geändert |
| Geheimnisse | Eigenschaften, die wie Passwort, Secret, API-Key, Token oder Connection String heißen, kommen maskiert; die Maske zurückzuschicken behält den gespeicherten Wert |
| Meta | das `meta` des Clients gruppiert, beschriftet, sortiert oder rendert Eigenschaften wie in jedem `MudExObjectEditForm`; Sperren und Ausblendungen des Servers gelten zusätzlich. Das Formular zeigt jeden Abschnitt unter seiner Überschrift in zwei Spalten (Listen über die volle Breite); `WrapInMudItem(i => i.md = 12)` verbreitert ein einzelnes Feld. Berechnete Eigenschaften (ohne Setter) entfallen, Felder haben kein eigenes Zurücksetzen; ein Listeneintrag in seinem Dialog (etwa ein Registrierungsdokument) folgt denselben Regeln |
| Gültigkeit | typisierte Einstellungen gelten für die ganze Installation und werden nur in der System-Organisation bearbeitet; Werte pro Mandant und Benutzer sind Einstellungen (oben) |

**Standardwerte wiederherstellen** verwirft die in der App geänderten Werte; appsettings und Umgebung gelten wieder.

### Editoren für Dateitypen, Größen, Zeitpläne und Übersetzungen

Vier Attribute aus `Coworkee.Contracts.Configuration` wählen für eine Eigenschaft einen passenderen Editor, auf der Einstellungsseite und in jedem anderen Objektformular der App:

| Attribut | Eigenschaft | Editor |
|---|---|---|
| `[ContentTypes]` | `List<string>` oder `string[]` mit MIME-Typen | Chips mit lesbaren Namen und Symbolen sowie eine Suche über alle MIME-Typen, die `MimeType` von Nextended kennt, nach Name, Dateiendung (`.epub`) oder MIME-Typ, jeder Treffer mit Symbol und Art (Bilder, Dokumente, Videos, Audio, Archive, Text, Schriftarten, 3D-Modelle, Sonstige). Platzhalter (`image/*`, `video/*`, `audio/*`, `text/*`) und gängige Typen stehen vorn; das Menü daneben fügt ganze Gruppen hinzu (alle Bilder, Office-Dokumente, ...). Sie zeigt höchstens 50 Treffer, damit der Editor schnell bleibt. Leer erlaubt jeden Typ |
| `[FileSize]` | `long?` oder `long` in Bytes | eine Zahl in KB, MB oder GB; leer heißt keine Begrenzung |
| `[Cron]` | `string` mit einem Cron-Ausdruck | Vorlagen (alle paar Minuten, stündlich, täglich, wöchentlich, monatlich) und der Ausdruck selbst, in Worten beschrieben (UTC); ein ungültiger Ausdruck wird nicht übernommen |
| `[Translations]` | `Dictionary<string, string>` von Kultur zu Text | eine Zeile pro Sprache mit Name und Code; die Sprachen der App zur Auswahl, andere Kulturcodes wie `fr-CA` werden eingetippt, Zeilen per Button entfernt |

```csharp title="Contracts"
public sealed class ImportSettings
{
    [Cron]
    public string Schedule { get; set; } = "0 2 * * *";

    [ContentTypes]
    public List<string> AcceptedFiles { get; set; } = ["text/csv"];

    [FileSize]
    public long? MaxFileSize { get; set; } = 10 * 1024 * 1024;

    [Translations]
    public Dictionary<string, string> Titles { get; set; } = [];
}
```

Die eingebauten Einstellungen nutzen sie für die Registrierungsdokumente (`Registration:Documents`, mit Namen und Beschreibungen pro Sprache) und die Uhrzeit der Zusammenfassung (`Notifications:DigestCron`). Ein `RenderWith` in der Client-`meta` hat weiterhin Vorrang vor dem Attribut.

### Beschriftungen, Hilfetexte und Gruppen aus `[Display]`

`[Display]` aus `System.ComponentModel.DataAnnotations` beschreibt eine Eigenschaft dort, wo sie deklariert ist, ohne Client-Code: `Name` ist die Beschriftung, `Description` der Hilfetext unter dem Feld und `GroupName` der Abschnitt, in dem sie steht. Wie jeder Text des Formulars laufen sie über die Übersetzungen; der deutsche Text gehört also in `de.json`.

```csharp
public sealed class BillingSettings
{
    [Display(Name = "Currency", GroupName = "Invoices", Description = "The currency of new invoices.")]
    public string Currency { get; set; } = "EUR";
}
```

Ein Registrierungsdokument zeigt, wie das aussieht: Sein Dialog hat die Abschnitte *Für den Benutzer sichtbar* (Name und Hinweis, gültig in jeder Sprache), *Übersetzungen (optional)* (Name und Hinweis für einzelne Sprachen, jede Zeile beschriftet mit *Übersetzung für Deutsch* usw.) und *Upload* (Pflicht, erlaubte Dateitypen, größte Datei).

### Weitere Abschnitte

Weitere Klassen erscheinen als weitere Tabs neben den App-Einstellungen:

```csharp title="Server"
builder.Configuration
    .AddCoworkeeAppConfigurationDefaults(SharemeConfiguration.Defaults(), SharemeConfiguration.Section)
    .AddCoworkeeDatabaseConfiguration("shareme");

context.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(context.Configuration, SharemeConfiguration.Section, SharemeConfiguration.Title,
    rules => rules.Lock(c => c.Storage));
```

```csharp title="Blazor-Client"
builder.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(SharemeConfiguration.Section, SharemeConfiguration.Title);
```

`AddCoworkeeAppConfigurationDefaults` legt eine JSON-Datei des Abschnitts als unterste Schicht an. Sharemee erzeugt `SharemeConfiguration` mit `Nextended.CodeGen` aus `appconfig.json`; JSON-Standardwerte und Klasse können so nicht auseinanderlaufen:

```json title="CodeGen.config.json"
{ "JsonConfigs": [ { "SourceFile": "Configuration/appconfig.json", "RootClassName": "SharemeConfiguration", "Namespace": "Shareme.Contracts.Configuration" } ] }
```

Die alte Adresse `/admin/configuration` führt zur Einstellungsseite.

## Dienste

`CoworkeeServicesOptions` (Abschnitt `Coworkee:Services`) benennt die Dienste der App: API, Auth-Server und Web-App, das Aspire-Dashboard, das Hangfire-Dashboard, Mailpit, pgAdmin und jede weitere Ressource mit Web-Endpunkt. Der [AppHost](../hosting/aspire.md) füllt sie in der Entwicklung; Deployments tragen ihre öffentlichen Adressen in appsettings ein:

```json
{ "Coworkee": { "Services": {
    "myapp-api": { "Url": "https://api.example.com", "HealthPath": "/health" },
    "grafana": { "Url": "https://grafana.example.com", "Title": "Grafana" } } } }
```

```csharp
public sealed class Links(IOptionsMonitor<CoworkeeServicesOptions> services)
{
    public string? Mailpit => services.CurrentValue.GetValueOrDefault("mail")?.Url;
}
```

**Administration > System > Dienste** zeigt sie als Kacheln mit Symbol, Adresse und aktuellem Zustand; ein Klick öffnet den Dienst in einem neuen Tab. Der Browser fragt die API (`GET /api/v1/services`, Einstellungs-Berechtigung, System-Organisation), die jeden Dienst alle zehn Sekunden prüft: Mit `HealthPath` zählt ein Erfolgsstatus als läuft, ohne jede Antwort unter 500.
