# Aspire-AppHost

`Coworkee.Aspire` macht aus dem AppHost wenige Zeilen. Sie nennen die App; Coworkee findet ihre Dienste, ergänzt die Infrastruktur, die jeder braucht, und die Einstellungen, die sie verbinden.

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.LogoUrl = "/coworkee-icon.svg";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("info@coworkee.de", "Administrator", "MyApp")));
}).AddProjects();

builder.Build().Run();
```

`LogoUrl` ist das Logo der Anmeldeseiten, solange das Theme keines hat: Ein Pfad wird auf der Web-App genommen (`{web}/coworkee-icon.svg`), eine absolute Adresse so, wie sie ist.

Sharemee ergänzt danach einen Worker, dessen Name keiner Konvention folgt:

```csharp
var app = builder.AddCoworkeeApp("shareme", ...).AddProjects();
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
```

## Projekte nach Konvention

`AddProjects()` schaut sich die `Projects.*`-Typen an, die Aspire für die referenzierten Projekte des AppHosts erzeugt, und fügt sie nach Namen in dieser Reihenfolge hinzu:

| Typname | Hinzugefügt mit | Ressource |
|---|---|---|
| `*_Migrations` | `AddMigrations` | `{name}-migrations` |
| `*_Auth` | `AddAuthServer` | `{name}-auth` |
| `*_Api` | `AddApi` | `{name}-api` |
| `*_Worker*`, `*Worker` | `AddWorker` | `{name}-worker` |
| `*_Web` | `AddWeb` | `{name}-web` |

Das Suffix ist der Teil nach dem ersten Unterstrich, kleingeschrieben mit Bindestrichen: Aus `MyApp_Jobs_Worker` wird `myapp-jobs-worker`. Andere Projekte bleiben unberührt.

```csharp
builder.AddCoworkeeApp("myapp", options =>
{
    options.Configure("api", api => api.WithReplicas(2)); // nach Suffix, auch für explizit hinzugefügte Dienste
    options.Skip("web", "MyApp_Admin_Api");                // nach Suffix oder Typname
}).AddProjects();
```

Explizite Aufrufe funktionieren weiter und haben Vorrang: Ein Projekt, das vor `AddProjects()` hinzugefügt wurde, kommt nicht doppelt hinzu. Soll ein gefundenes Projekt eine andere Rolle oder ein anderes Suffix bekommen, überspringen Sie es und fügen es danach selbst hinzu.

## Rollen

| Methode | Ressource | Verdrahtung |
|---|---|---|
| `AddMigrations<T>()` | `{name}-migrations` | Datenbank, wartet auf den Postgres-Server, Dashboard-Befehle |
| `AddAuthServer<T>()` | `{name}-auth` | externe Endpunkte, kein Job-Server, öffentliche Auth-URL, Entwicklungszertifikate in Development, Keycloak-Anbieter |
| `AddApi<T>()` | `{name}-api` | Token-Authority und Audience `{name}_api`, API-Scope am Auth-Server, Setup-Token aus `Coworkee:SetupToken` |
| `AddWorker<T>(suffix)` | `{name}-{suffix}` | nur Infrastruktur, keine öffentlichen Endpunkte |
| `AddWeb<T>()` | `{name}-web` | BFF-Einstellungen, OIDC-Client am Auth-Server mit Redirect-URIs, Referenz auf die API, Links zu Swagger und zum Job-Dashboard |

Rufen Sie sie in dieser Reihenfolge auf (`AddProjects()` tut das). `AddWeb` vor `AddAuthServer` wirft eine Exception, die genau das sagt.

## Infrastruktur aus den Paketen

Bevor Coworkee einen Dienst verdrahtet, liest es dessen `obj/project.assets.json` und schaut, welche Pakete und Projekte er nutzt, direkt oder über andere Projekte:

| Paket im Dienst | Er bekommt |
|---|---|
| `Coworkee.Infrastructure` | Connection-String der App-Datenbank, wartet, bis die Migrationen fertig sind |
| `Coworkee.Realtime` | Redis für die SignalR-Backplane, die Sitzungsänderungen zwischen Instanzen und den gemeinsamen Cache der Sitzungs-Stamps |
| `Coworkee.Mailing` | Mailpit als Standard-SMTP-Server |
| `Coworkee.Storage` | den gemeinsamen Blob-Ordner `.data/blobs` (nur lokal; veröffentlichte Container behalten ihren Standard) |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |
| `Coworkee.Notifications` | Links in Zusammenfassungsmails zeigen auf die Web-App |

Container entstehen nur, wenn ein Dienst sie braucht. Postgres, Redis, Elasticsearch und Keycloak halten ihre Daten in Volumes; `--Coworkee:EphemeralInfrastructure=true` startet sie leer, das nutzen die Tests.

### Eigene Module

`app.Modules` enthält diese Verdrahtung nach Paket- oder Projektname. App-Module melden ihren Bedarf genauso an; jeder Dienst, der das Projekt direkt oder transitiv referenziert, bekommt ihn:

```csharp
var app = builder.AddCoworkeeApp("myapp");
var stripe = builder.AddConnectionString("stripe");
app.Modules.Add("MyApp.Billing", (app, service) => service.WithReference(stripe));
app.AddProjects();
```

Registrieren Sie Module, bevor Sie die Dienste hinzufügen. `app.Modules[CoworkeeModules.Search] = ...` ersetzt eine eingebaute Verdrahtung, `app.Modules.Remove(...)` entfernt sie.

## Eigene Infrastruktur

Jedes Teil lässt sich konfigurieren, ersetzen oder abschalten:

```csharp
builder.AddCoworkeeApp("myapp", options =>
{
    options.ConfigurePostgres(postgres => postgres.WithPgAdmin())
        .ConfigureRedis(redis => redis.WithRedisInsight())
        .ConfigureMail(mail => mail.WithLifetime(ContainerLifetime.Persistent))
        .ConfigureSearch(search => search.WithEnvironment("ES_JAVA_OPTS", "-Xmx1g"))
        .ConfigureKeycloak(keycloak => keycloak.WithLifetime(ContainerLifetime.Persistent));

    // Dienste mit Coworkee.Search.Elasticsearch bekommen kein Elasticsearch
    options.Without(CoworkeeModules.Search);
}).AddProjects();
```

| Option | Statt |
|---|---|
| `UseDatabase(resource)` | des Postgres-Containers; jede Ressource mit Connection-String, z. B. `builder.AddConnectionString("myapp")` |
| `UseRedis(resource)` | des Redis-Containers |
| `UseSearch(resource)` | des Elasticsearch-Containers |
| `UseMail(endpoint)` | von Mailpit; der SMTP-Endpunkt einer anderen Ressource |
| `UseKeycloak(resource, ...)` | des Keycloak-Containers; der Realm wird in Ihren importiert |

Die Dienste bekommen eigene Ressourcen unter den Namen, die sie erwarten (`ConnectionStrings:myapp`, `redis`, `elasticsearch`). Coworkee wartet nicht auf sie; ergänzen Sie bei Bedarf `WaitFor` über `options.Configure`.

```csharp
// Produktion: der Connection-String kommt aus der Konfiguration (ConnectionStrings:myapp) oder wird beim Deployment abgefragt
var database = builder.AddConnectionString("myapp");
builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database)).AddProjects();
```

## Dashboard

Läuft der AppHost lokal in Development, hat die Migrations-Ressource zwei Befehle:

- **Re-run migrations** startet das Migrationsprojekt erneut: neue Migrationen und das Seeding der Demodaten.
- **Reset database** löscht nach einer Rückfrage die App-Datenbank und führt die Migrationen erneut aus. Den Befehl gibt es nur für den eingebauten Postgres-Container, nie für eine eigene Datenbank und nie außerhalb von Development.

Die Web-Ressource verlinkt **Swagger** (`/swagger`) und, wenn eine API `Coworkee.BackgroundJobs` nutzt, das Hangfire-Dashboard (**Jobs**, `/admin/jobs`). Mailpit zeigt seinen Posteingang als **Mailpit UI**.

## Dienstadressen

Jeder Dienst der App bekommt die Adressen der anderen unter `Coworkee:Services:<Ressource>`: das Aspire-Dashboard (`dashboard`), das Hangfire-Dashboard (`jobs`), jedes Projekt (`myapp-api`, `myapp-auth`, `myapp-web`, mit `HealthPath` `/health`) und jeden Container mit http-Endpunkt (`mail`, `pgadmin`, `keycloak`, ...), auch Ressourcen, die nach `AddCoworkeeApp` hinzukommen. Injiziert werden sie als `IOptionsMonitor<CoworkeeServicesOptions>`; die Admin-Seite **Dienste** zeigt sie mit ihrem aktuellen Zustand (siehe [Einstellungen](../modules/settings.md#dienste)). Nur der lokale Lauf füllt sie; eine veröffentlichte App trägt ihre öffentlichen Adressen in appsettings ein.

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

## Veröffentlichen

`PublishToDockerCompose()` fügt eine Docker-Compose-Umgebung hinzu. `aspire publish` schreibt dann eine `docker-compose.yaml` und eine `.env`-Datei mit allen Diensten, Containern, Volumes und der Startreihenfolge (Dienste starten, sobald die Migrationen abgeschlossen sind):

```csharp
builder.AddCoworkeeApp("myapp", options => options.PublishToDockerCompose(compose => compose.WithDashboard(false)))
    .AddProjects();
```

```bash
aspire publish -o deploy
```

Azure Container Apps funktioniert über die austauschbare Infrastruktur; nehmen Sie `Aspire.Hosting.Azure.AppContainers` und `Aspire.Hosting.Azure.PostgreSQL` in den AppHost auf:

```csharp
builder.AddAzureContainerAppEnvironment("aca");
var database = builder.AddAzurePostgresFlexibleServer("postgres").RunAsContainer().AddDatabase("myapp");
builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database)).AddProjects();
```

## Produktion

Der AppHost beschreibt die Entwicklung. Für die Produktion konfigurieren Sie zusätzlich echte Zertifikate (`Coworkee:Auth:SigningCertificate`, `EncryptionCertificate`), die öffentlichen URLs hinter Ihrem Reverse Proxy, einen echten SMTP-Server, Speicher und Keycloak. Das Dashboard eines lokalen Laufs listet die Einstellungen, die jeder Dienst braucht.
