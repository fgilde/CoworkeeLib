# Aspire-AppHost

`Coworkee.Aspire` macht aus dem AppHost wenige Zeilen. Sie nennen die App; Coworkee findet ihre Dienste, ergänzt die Infrastruktur, die jeder braucht, und die Einstellungen, die sie verbinden.

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.LogoUrl = "/coworkee-icon.svg";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("info@coworkee.de", "Administrator", "MyApp") { Password = "Dev-Passw0rd!" }));
}).AddProjects();

builder.Build().Run();
```

`LogoUrl` ist das Logo der Anmeldeseiten, solange das Theme keines hat: Ein Pfad wird auf der Web-App genommen (`{web}/coworkee-icon.svg`), eine absolute Adresse so, wie sie ist.

`options.SignIn` setzt Layout, Medium und Seite des Mediums der [Anmeldeseiten](../security/sign-in-pages.md) (`Coworkee:Auth:Design` des Auth-Servers); ein Pfad zum Medium wird wie `LogoUrl` auf der Web-App aufgelöst:

```csharp
options.SignIn.Layout = SignInLayouts.Split;
options.SignIn.MediaUrl = "/media/welcome.mp4";
options.SignIn.MediaSide = SignInMediaSide.Right;
```

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
| `AddMigrations<T>()` | `{name}-migrations` | Datenbank, wartet auf den Datenbankserver, Dashboard-Befehle |
| `AddAuthServer<T>()` | `{name}-auth` | externe Endpunkte, kein Job-Server, öffentliche Auth-URL, Entwicklungszertifikate in Development, Keycloak-Anbieter |
| `AddApi<T>()` | `{name}-api` | Token-Authority und Audience `{name}_api`, API-Scope am Auth-Server, Setup-Token aus `Coworkee:SetupToken` |
| `AddWorker<T>(suffix)` | `{name}-{suffix}` | nur Infrastruktur, keine öffentlichen Endpunkte |
| `AddWeb<T>()` | `{name}-web` | BFF-Einstellungen, OIDC-Client am Auth-Server mit Redirect-URIs, Referenz auf die API, Links zu Swagger und zum Job-Dashboard |

Rufen Sie sie in dieser Reihenfolge auf (`AddProjects()` tut das). `AddWeb` vor `AddAuthServer` wirft eine Exception, die genau das sagt.

## Infrastruktur aus den Paketen

Bevor Coworkee einen Dienst verdrahtet, liest es dessen `obj/project.assets.json` und schaut, welche Pakete und Projekte er nutzt, direkt oder über andere Projekte:

| Paket im Dienst | Er bekommt |
|---|---|
| `Coworkee.Infrastructure` | Connection-String der App-Datenbank und `Coworkee:Database:Provider`, wartet, bis die Migrationen fertig sind |
| `Coworkee.Realtime` | Redis für die SignalR-Backplane, Anwesenheit, Cache-Invalidierung und Sitzungsänderungen zwischen Instanzen ([Skalieren mit Redis](redis.md)); `options.WithoutRedis()` lässt es weg |
| `Coworkee.Mailing` | Mailpit als Standard-SMTP-Server |
| `Coworkee.Storage` | den gemeinsamen Blob-Ordner `.data/blobs` (nur lokal; veröffentlichte Container behalten ihren Standard) |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |
| `Coworkee.Notifications` | Links in Zusammenfassungsmails zeigen auf die Web-App |

Grafana (mit Prometheus, Loki, Tempo und Collector) und WebDataStudio kommen mit `options.UseGrafana()` und `options.UseWebDataStudio()` dazu, siehe [Grafana und WebDataStudio](observability.md).

Container entstehen nur, wenn ein Dienst sie braucht. Postgres, SQL Server, Redis, Elasticsearch und Keycloak halten ihre Daten in Volumes; `--Coworkee:EphemeralInfrastructure=true` startet sie leer, das nutzen die Tests.

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
| `UseDatabase(resource)` | des Datenbank-Containers; jede Ressource mit Connection-String, z. B. `builder.AddConnectionString("myapp")` |
| `UseSqlServer(configure)` | von Postgres: ein SQL-Server-Container (`app.SqlServer`), oder mit `UseDatabase` ein eigener SQL Server |
| `UseSqlite(path)` | von Postgres: eine SQLite-Datei, ohne Angabe `.data/<name>.db` neben `src`; kein Container |
| `UseRedis(resource)` | des Redis-Containers |
| `UseSearch(resource)` | des Elasticsearch-Containers |
| `UseMail(endpoint)` | von Mailpit; der SMTP-Endpunkt einer anderen Ressource |
| `UseKeycloak(resource, ...)` | des Keycloak-Containers; Ihr Keycloak bekommt den Realm |

`UseSqlServer` und `UseSqlite` setzen bei den Diensten auch `Coworkee:Database:Provider`; die Migrationen der App müssen für diesen Provider erstellt sein (siehe [Datenbank-Provider](../fundamentals/persistence.md#datenbank-provider)).

Die Dienste bekommen eigene Ressourcen unter den Namen, die sie erwarten (`ConnectionStrings:myapp`, `redis`, `elasticsearch`). Coworkee wartet nicht auf sie; ergänzen Sie bei Bedarf `WaitFor` über `options.Configure`.

```csharp
// Produktion: der Connection-String kommt aus der Konfiguration (ConnectionStrings:myapp) oder wird beim Deployment abgefragt
var database = builder.AddConnectionString("myapp");
builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database)).AddProjects();
```

## Dashboard

Läuft der AppHost lokal in Development, hat die Migrations-Ressource zwei Befehle:

- **Re-run migrations** startet das Migrationsprojekt erneut: neue Migrationen und das Seeding der Demodaten.
- **Reset database** löscht nach einer Rückfrage die App-Datenbank und führt die Migrationen erneut aus. Den Befehl gibt es nur für den eingebauten Postgres-Container, nie für SQL Server, SQLite oder eine eigene Datenbank und nie außerhalb von Development.

Die Web-Ressource verlinkt **Swagger** (`/swagger`) und, wenn eine API `Coworkee.BackgroundJobs` nutzt, das Hangfire-Dashboard (**Jobs**, `/admin/jobs`). Mailpit zeigt seinen Posteingang als **Mailpit UI**.

## Dienstadressen

Jeder Dienst der App bekommt die Adressen der anderen unter `Coworkee:Services:<Ressource>`: das Aspire-Dashboard (`dashboard`), das Hangfire-Dashboard (`jobs`), jedes Projekt (`myapp-api`, `myapp-auth`, `myapp-web`, mit `HealthPath` `/health`) und jeden Container mit http-Endpunkt (`mail`, `pgadmin`, `keycloak`, ...), auch Ressourcen, die nach `AddCoworkeeApp` hinzukommen. Injiziert werden sie als `IOptionsMonitor<CoworkeeServicesOptions>`; die Admin-Seite **Dienste** zeigt sie mit ihrem aktuellen Zustand (siehe [Einstellungen](../modules/settings.md#dienste)). Nur der lokale Lauf füllt sie; eine veröffentlichte App trägt ihre öffentlichen Adressen in appsettings ein.

Ein AspireUI im AppHost bringt die Apps und Stacks, die Sie *selbst* damit hosten, auf dieselbe Seite. `builder.AddAspireUI().WithRetrieveApi()` genügt: Der AppHost gibt die Adresse (und den Schlüssel aus `WithRetrieveApi(...)`: Ein Parameter bleibt eine Referenz, nie ein aufgelöster String; ein Klartext-Schlüssel wird als Text weitergegeben) als `Coworkee:AspireUI:Url` und `ApiKey` an jeden Dienst weiter. Das erste AspireUI mit Retrieve-API wird verwendet; eines ohne wird ignoriert, und eine veröffentlichte App bekommt nichts (`Coworkee:AspireUI` dann in appsettings setzen). Dienste finden sie über `IServiceDirectory` (siehe [Einstellungen](../modules/settings.md#dienstverzeichnis)).

## Typisierte Einstellungen

Einstellungen werden über den Konfigurationsbaum gesetzt, nicht über Zeichenketten:

```csharp
api.WithSetting(s => s.Coworkee.Storage.Provider, "S3")
   .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], "smtp.example.com")
   .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/admin/jobs", "/hubs");
```

Aus `s => s.Coworkee.Bff.Scopes[0]` wird die Umgebungsvariable `Coworkee__Bff__Scopes__0`. Werte können Zeichenketten, Zahlen, Wahrheitswerte, Endpunkt-Referenzen, Referenzausdrücke oder Parameter sein. `CoworkeeSettings` spiegelt die echten Options-Klassen aus `Coworkee.Contracts.Configuration`; eine umbenannte Eigenschaft bricht also den Build des AppHosts, nicht die laufende App.

## Keycloak

`UseKeycloak` fügt einen Keycloak-Container mit einem Realm hinzu, der wie die App heißt:

- ein vertraulicher Client `{name}-auth`, dessen Secret ein generierter Parameter ist (`{name}-keycloak-client-secret`),
- die angegebenen Benutzer, jeder mit seinem `Password`; Benutzer ohne teilen sich ein generiertes Passwort (`{name}-keycloak-user-password`, im Dashboard sichtbar und in den User-Secrets des AppHosts gespeichert).

Der Realm wird bei **jedem Start** auf den aktuellen Stand gebracht, sobald Keycloak bereit ist und bevor der Auth-Server startet: Ein fehlender Realm wird angelegt, ein vorhandener (im Daten-Volume) bekommt über die Admin-API die aktuellen Client-Secrets, Redirect-Adressen, Benutzer und Passwörter. Ein geänderter Secret-Parameter oder ein geändertes Passwort gilt also ab dem nächsten Start, und Benutzer oder Clients, die Sie in der Keycloak-Konsole angelegt haben, bleiben erhalten. Das Ressourcen-Log von Keycloak meldet `Realm myapp is up to date: clients ..., users ...`.

Der Auth-Server bekommt die Anbieter-Einstellungen und bietet **Sign in with Keycloak** an. `LoginMode` schaltet zwischen beidem, nur extern und nur intern um und ist der Standard der Einstellung *Anmeldemethoden* (`Auth.LoginMode`), die Administratoren zur Laufzeit ändern (siehe [Anmelderegeln](../security/registration.md#anmelderegeln)); `Port` legt den Host-Port fest, wenn Sie eine feste URL wollen.

Die Api-Hosts liefern die Einstellungen, also bekommen auch sie `Coworkee:Auth:External:Mode` und den Anzeigenamen des Anbieters, damit die Einstellung dort definiert ist; das Client-Secret und die übrigen Anbieter-Einstellungen bleiben beim Auth-Server.

Browser schicken die Cookies von `localhost` an jeden Port, also erreichen die Cookies aller lokalen Apps auch Keycloak. Über HTTPS spricht Keycloak HTTP/2 und nimmt dabei standardmäßig nur 8 KB Request-Header an; darüber antwortet es mit **431 Request Header Fields Too Large**. Der Container bekommt deshalb `QUARKUS_HTTP_LIMITS_MAX_HEADER_LIST_SIZE=65536`, die 64 KB, die er über HTTP/1.1 annimmt. Zeigt der Browser trotzdem 431, löschen Sie die `localhost`-Cookies anderer Apps; Auth-Server und Web-App (Kestrel) nehmen 32 KB an.

### Weitere Clients und die Benutzer

Andere Ressourcen des AppHosts können sich über denselben Realm anmelden und seine Benutzer wiederverwenden, zum Beispiel ein Dashboard mit OpenID Connect:

```csharp
var app = builder.AddCoworkeeApp("myapp", options => options.UseKeycloak(k => k.Users.AddRange(users))).AddProjects();
var grafana = builder.AddContainer("grafana", "grafana/grafana").WithHttpEndpoint(targetPort: 3000);
var client = app.AddKeycloakClient("myapp-grafana", [ReferenceExpression.Create($"{grafana.GetEndpoint("http")}/login/generic_oauth")]);
grafana.WithEnvironment("GF_AUTH_GENERIC_OAUTH_CLIENT_ID", client.ClientId)
    .WithEnvironment("GF_AUTH_GENERIC_OAUTH_CLIENT_SECRET", client.Secret)
    .WithEnvironment("GF_AUTH_GENERIC_OAUTH_AUTH_URL", ReferenceExpression.Create($"{app.KeycloakAuthority}/protocol/openid-connect/auth"));
```

| Member von `CoworkeeApp` | |
|---|---|
| `AddKeycloakClient(clientId, redirectUris?, secret?)` | ein weiterer vertraulicher Client des Realms; ohne Secret ein generiertes (`{clientId}-secret`), ohne Redirect-Adressen jede lokale Adresse |
| `KeycloakClients` | die Clients des Realms, der des Auth-Servers zuerst |
| `KeycloakAuthority` | Issuer-Adresse des Realms, `{keycloak}/realms/{realm}` |
| `KeycloakUsers`, `KeycloakPassword(user)` | die Benutzer und das Passwort jedes Benutzers als Reference Expression, etwa für den lokalen Login einer anderen Ressource |

Rufen Sie sie nach `UseKeycloak` und `AddAuthServer` (oder `AddProjects`) auf.

### AspireUI

[AspireUI](https://github.com/fgilde/AspireUI) meldet sich mit einem Aufruf über den Realm an; die Administratoren der App (Realm-Rolle `Administrator`) werden AspireUI-Admins, alle anderen bekommen `defaultPermissions` (Standard `app-user`):

```csharp
using Nextended.Aspire.Hosting.AspireUI;

var app = builder.AddCoworkeeApp("myapp", options => options.UseKeycloak(...)).AddProjects();
builder.AddAspireUI().WithCoworkeeSignOn(app);
```

Der Aufruf registriert den Client `{app}-{resource}` mit der Redirect-Adresse `/api/auth/sso/callback`, übergibt den Realm so, wie der Container ihn erreicht (Keycloak nennt sich dann nach der Adresse des Browsers, `KC_HOSTNAME`), und setzt den Rollen-Claim als Gruppen. AspireUI übernimmt Einstellungen nur, solange sie leer sind; deshalb erzwingt der Aufruf die Einstellungen des App-Hosts (`ASPIREUI_SET_FORCE`), sonst behielte ein Daten-Volume aus einem früheren Realm die alte Anmeldung.

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
