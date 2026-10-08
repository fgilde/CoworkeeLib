# Aspire-AppHost

`Coworkee.Aspire` macht aus dem AppHost wenige Zeilen. Sie nennen die Dienste und ihre Rollen; Coworkee ergänzt die Infrastruktur, die jeder braucht, und die Einstellungen, die sie verbinden.

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

var app = builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("info@coworkee.de", "Administrator", "MyApp")));
});

app.AddMigrations<Projects.MyApp_Migrations>();
app.AddAuthServer<Projects.MyApp_Auth>();
app.AddApi<Projects.MyApp_Api>();
app.AddWeb<Projects.MyApp_Web>();

builder.Build().Run();
```

Sharemee ergänzt einen Worker für seine Verarbeitungs-Warteschlange:

```csharp
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
```

## Rollen

| Methode | Ressource | Verdrahtung |
|---|---|---|
| `AddMigrations<T>()` | `{name}-migrations` | Datenbank, wartet auf den Postgres-Server |
| `AddAuthServer<T>()` | `{name}-auth` | externe Endpunkte, kein Job-Server, öffentliche Auth-URL, Entwicklungszertifikate in Development, Keycloak-Anbieter |
| `AddApi<T>()` | `{name}-api` | Token-Authority und Audience `{name}_api`, API-Scope am Auth-Server, Setup-Token aus `Coworkee:SetupToken` |
| `AddWorker<T>(suffix)` | `{name}-{suffix}` | nur Infrastruktur, keine öffentlichen Endpunkte |
| `AddWeb<T>()` | `{name}-web` | BFF-Einstellungen, OIDC-Client am Auth-Server mit Redirect-URIs, Referenz auf die API, öffentliche App-URL für Benachrichtigungsmails |

Rufen Sie sie in dieser Reihenfolge auf. `AddWeb` vor `AddAuthServer` wirft eine Exception, die genau das sagt.

## Infrastruktur aus den Paketen

Bevor Coworkee einen Dienst verdrahtet, liest es dessen `obj/project.assets.json` und schaut, welche Coworkee-Pakete er nutzt, direkt oder über andere Projekte:

| Paket im Dienst | Er bekommt |
|---|---|
| `Coworkee.Infrastructure` | Connection-String der App-Datenbank, wartet, bis die Migrationen fertig sind |
| `Coworkee.Realtime` | Redis für die SignalR-Backplane |
| `Coworkee.Mailing` | Mailpit als Standard-SMTP-Server |
| `Coworkee.Storage` | den gemeinsamen Blob-Ordner `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |
| `Coworkee.Notifications` | Links in Zusammenfassungsmails zeigen auf die Web-App |

Container entstehen nur, wenn ein Dienst sie braucht. Postgres, Redis, Elasticsearch und Keycloak halten ihre Daten in Volumes; `--Coworkee:EphemeralInfrastructure=true` startet sie leer, das nutzen die Tests.

## Typisierte Einstellungen

Einstellungen werden über den Konfigurationsbaum gesetzt, nicht über Zeichenketten:

```csharp
api.WithSetting(s => s.Coworkee.Storage.Provider, "S3")
   .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], "smtp.example.com")
   .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/admin/jobs", "/hubs");
```

Aus `s => s.Coworkee.Bff.Scopes[0]` wird die Umgebungsvariable `Coworkee__Bff__Scopes__0`. Werte können Zeichenketten, Zahlen, Wahrheitswerte, Endpunkt-Referenzen, Referenzausdrücke oder Parameter sein. `CoworkeeSettings` spiegelt die echten Options-Klassen aus `Coworkee.Contracts.Configuration`; eine umbenannte Eigenschaft bricht also den Build des AppHosts, nicht die laufende App.

## Keycloak

`UseKeycloak` fügt einen Keycloak-Container hinzu und importiert einen Realm mit dem Namen der App mit

- einem vertraulichen Client `{name}-auth`, dessen Secret ein generierter Parameter ist (`{name}-keycloak-client-secret`),
- den angegebenen Benutzern, alle mit einem generierten Passwort (`{name}-keycloak-user-password`, im Dashboard sichtbar und in den User-Secrets des AppHosts gespeichert).

Der Auth-Server bekommt die Anbieter-Einstellungen und bietet **Sign in with Keycloak** an. `LoginMode` schaltet zwischen beidem, nur extern und nur intern um; `Port` legt den Host-Port fest, wenn Sie eine feste URL wollen.

## Produktion

Der AppHost beschreibt die Entwicklung. Für die Produktion dient das Dashboard eines lokalen Laufs als Liste dessen, was jeder Dienst braucht; konfigurieren Sie echte Zertifikate (`Coworkee:Auth:SigningCertificate`, `EncryptionCertificate`), einen echten SMTP-Server, Speicher und Keycloak.
