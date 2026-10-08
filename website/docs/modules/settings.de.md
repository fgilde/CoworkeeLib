# Einstellungen und App-Konfiguration

Coworkee kennt zwei Arten von Konfiguration, die Administratoren zur Laufzeit ändern.

| | Einstellungen | App-Konfiguration |
|---|---|---|
| Für | einzelne Werte pro Installation, Mandant oder Benutzer (SMTP-Host, Theme, Registrierung an/aus) | eine typisierte Options-Klasse der App (Grenzen, Schalter, Integrationen) |
| Definiert durch | `ISettingDefinitionContributor` | eine C#-Klasse, optional aus JSON erzeugt |
| Gespeichert in | Einstellungstabelle, Geheimnisse verschlüsselt | `cw.ConfigurationEntries`, gelesen über `IConfiguration` |
| Gelesen mit | `ISettingProvider` | `IOptionsMonitor<T>` |
| Admin-Seite | Settings | Configuration (nur System-Mandant) |

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

Typen sind `String`, `Text`, `Bool`, `Int`, `Secret` und `Choice`; Gültigkeitsbereiche `Global`, `Tenant` und `User`. Der spezifischste Wert gewinnt. Geheimnisse werden mit ASP.NET Core Data Protection verschlüsselt und nie an den Client geschickt; Einstellungen mit `visibleToClient: true` schon.

```csharp
var allowed = await settings.GetAsync<bool>(AccountSettings.AllowRegistration, ct);
```

Standardwerte können auch aus der Konfiguration kommen: `Coworkee:Settings:Defaults:Mail.Smtp.Host`. Der AppHost nutzt das für Mailpit.

![Einstellungsseite](../assets/screenshots/settings.png){ .shot }

## Typisierte App-Konfiguration

Die App beschreibt ihre Konfiguration als Klasse. Der Datenbank-Provider liegt über `appsettings.json`: Die Datei hält die Standardwerte, Administratoren ändern Werte ohne Deployment.

```csharp title="Server"
builder.Configuration
    .AddCoworkeeAppConfigurationDefaults(SharemeConfiguration.Defaults(), SharemeConfiguration.Section)
    .AddCoworkeeDatabaseConfiguration("shareme");

context.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(context.Configuration, SharemeConfiguration.Section, SharemeConfiguration.Title);
```

```csharp title="Blazor-Client"
builder.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(SharemeConfiguration.Section, SharemeConfiguration.Title);
```

Die Seite Configuration zeigt die Klasse mit `MudExObjectEditForm`. Dienste lesen `IOptionsMonitor<SharemeConfiguration>` und sehen Änderungen nach wenigen Sekunden.


Sharemee erzeugt `SharemeConfiguration` mit `Nextended.CodeGen` aus `appconfig.json`; JSON-Standardwerte und Klasse können so nicht auseinanderlaufen:

```json title="CodeGen.config.json"
{ "JsonConfigs": [ { "SourceFile": "Configuration/appconfig.json", "RootClassName": "SharemeConfiguration", "Namespace": "Shareme.Contracts.Configuration" } ] }
```
