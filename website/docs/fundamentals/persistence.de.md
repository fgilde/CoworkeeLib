# Persistenz

Die App hat genau einen `DbContext`, abgeleitet von `CoworkeeDbContext`. Module tragen ihre Entities bei; Coworkee bringt die Tabellen für Identity, Einstellungen, Audit und Outbox mit.

```csharp title="MyApp.Infrastructure/MyAppDbContext.cs"
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
```

```csharp title="MyApp.Infrastructure/MyAppInfrastructureModule.cs"
context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) =>
    options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "myapp"));
```

`UseCoworkeeDatabase` wählt den EF-Core-Provider nach `Coworkee:Database:Provider` (siehe [Datenbank-Provider](#datenbank-provider)) und führt die Migrationshistorie in `cw.__EFMigrationsHistory`.

Handler injizieren `CoworkeeDbContext` und arbeiten mit `db.Set<T>()`. Repositories gibt es nicht; EF Core ist bereits eines.

## Datenbank-Provider

| `Coworkee:Database:Provider` | EF-Core-Provider | Aspire-AppHost | Passt für |
|---|---|---|---|
| `Postgres` (Standard) | Npgsql | `AddCoworkeeApp` startet einen Postgres-Container | alles, Produktion |
| `SqlServer` | Microsoft.EntityFrameworkCore.SqlServer | `options.UseSqlServer()`, ein SQL-Server-Container | Organisationen mit SQL Server oder Azure SQL |
| `Sqlite` | Microsoft.EntityFrameworkCore.Sqlite | `options.UseSqlite()`, die Datei `.data/<name>.db` | Demos, Prototypen, ein Rechner |

Der AppHost setzt die Einstellung bei jedem Dienst, neben dem Connection-String. Anderswo (Tests, eigenes Hosting) setzt du sie wie jeden anderen Konfigurationswert. `coworkee new --database sqlServer` schreibt den Aufruf im AppHost und legt die erste Migration für diesen Provider an.

Die providerspezifischen Teile liegen hinter kleinen Nahtstellen in `Coworkee.Infrastructure`, in der App ist nichts zu tun:

| Teil | Postgres | SQL Server | SQLite |
|---|---|---|---|
| `IHasConcurrencyToken.Version` | `xmin` | Spalte, bei jedem Update hochgezählt | Spalte, bei jedem Update hochgezählt |
| `HasColumnType("jsonb")` | `jsonb` | `nvarchar(max)` | `TEXT` |
| Outbox-Stapel | `FOR UPDATE SKIP LOCKED` | `UPDLOCK, READPAST` | einfache Abfrage, keine Zeilensperren |
| `IDistributedLock` | Advisory Lock | `sp_getapplock` | Dateisperre im Temp-Ordner (ein Rechner) |
| Hintergrundjobs | Hangfire.PostgreSql | Hangfire.SqlServer | Hangfire.InMemory, jeder Prozess hat seinen eigenen Job-Server |
| Textvergleich | Groß-/Kleinschreibung zählt | Groß-/Kleinschreibung zählt (`Latin1_General_100_CS_AS` auf jeder Textspalte) | Groß-/Kleinschreibung zählt (`=`) |

Grenzen, die man kennen sollte:

- **Migrationen gehören zu einem Provider.** Eine App behält die Migrationen des Providers, mit dem sie angelegt wurde. Zum Wechseln löschst du `Migrations` und führst `coworkee migrations add Initial` erneut aus. Soll eine App mehrere Provider bedienen, folge der EF-Core-Anleitung [Migrations with multiple providers](https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers) mit einer Migrations-Assembly pro Provider.
- **[Datenbanksicherungen](../modules/backups.md) brauchen Postgres.** Das Template lässt das Modul bei den anderen Providern weg.
- **SQLite** kennt nur einen Schreiber zur Zeit, hält Hintergrundjobs im Speicher (eingereihte und verzögerte Jobs gehen beim Neustart verloren), speichert `DateTimeOffset` als Zahl und `decimal` als `double`, prüft `HasMaxLength` nicht und kennt keine Schemas (Tabellen verlieren ihr `cw.`). Es ist für Entwicklung und kleine Installationen auf einem Rechner gedacht.
- MySQL und MariaDB gibt es nicht: Pomelo, der übliche Provider, hat noch kein Release für EF Core 10.

## Basisklassen und Marker

| Typ | Liefert |
|---|---|
| `Entity` | `Id`, eine GUID Version 7, beim Erzeugen gesetzt |
| `AuditedEntity` / `AuditedAggregateRoot` | `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`, beim Speichern gefüllt |
| `AggregateRoot` | `Raise(IDomainEvent)` für die Outbox |
| `IMultiTenant` | Abfragefilter auf den aktuellen Mandanten, `TenantId` beim Anlegen gesetzt |
| `ISoftDelete` | `Remove` markiert die Zeile als gelöscht; Abfragen blenden sie aus |
| `IHasConcurrencyToken` | optimistische Nebenläufigkeit über eine `Version`-Spalte |
| `IVersioned` | hält pro Revision einen Schnappschuss für die Versionshistorie |
| `[Realtime(permission)]` | meldet Anlegen, Ändern und Löschen an SignalR-Clients mit der Berechtigung |
| `[NotAudited]`, `[Sensitive]` | nimmt einen Typ oder ein Feld aus dem Audit oder protokolliert ein Feld maskiert |

## Audit

Jede Änderung an einer auditierten Entity wird Feld für Feld mit Benutzer, Zeitpunkt sowie altem und neuem Wert gespeichert. Die Admin-Oberfläche zeigt das pro Entity (`AuditTimeline`) und gesamt (Seite Audit-Log).

Ein Interceptor, der beim Speichern Zeilen ergänzt, registriert sich als `IPreparingInterceptor` (`services.AddScoped<IPreparingInterceptor, MyDefaults>()`) statt als `IInterceptor`: Er läuft vor den eingebauten, so dass seine Zeilen Mandanten- und Auditfelder, Audit-Einträge, Historie, Outbox-, Echtzeit- und Webhook-Nachrichten bekommen. Das Identity-Modul ergänzt so die Standardrollen und die Standardgruppe eines neuen Benutzers.

## Mandanten und aktueller Benutzer

`ICurrentUser` trägt Benutzer-ID, Mandanten-ID und Berechtigungen des Requests. Hintergrundjobs und die Outbox stellen den Benutzer wieder her, der sie gestartet hat. Wer als jemand anderes handeln muss, etwa in einem Seed, öffnet einen Scope:

```csharp
using (CurrentUserScope.Begin(new ImpersonatedUser(adminId, tenantId)))
{
    // Abfragen und Anlegen laufen als dieser Benutzer und Mandant
}
```

## Externe Datenbanken

Eine App kann auch eine Datenbank lesen und schreiben, die ihr nicht gehört, etwa die Datenbank eines anderen Teams mit eigenem Schema. Sie wird in einem einfachen `DbContext` abgebildet (nicht `CoworkeeDbContext`: der brächte die Tabellen und Konventionen der Bibliothek mit), wie üblich registriert und Coworkee bekannt gemacht:

```csharp title="MyApp.Shipping/ShippingModule.cs"
services.AddDbContextPool<HubDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("Hub")));
services.AddCoworkeeExternalDbContext<HubDbContext>();
services.AddODataEntity<Shipment>("Shipments", ShippingPermissions.Shipments.View);
services.AddScoped<IODataEntityFilter<Shipment>, ShipmentsOfMyCustomers>();
```

- OData-Sets, Zeilenfilter, globale Suche, Export, Berichte und Client-Entities lesen jede Entity aus dem Context, der sie abbildet.
- Handler lassen sich den externen Context geben und ändern ihn; die Unit of Work speichert zuerst die externen Contexts mit Änderungen, dann den Context der App. Eine Transaktion über beide Datenbanken gibt es nicht: scheitert der Context der App, bleiben die externen Änderungen gespeichert.
- Migrationen, Audit, Verlauf, Soft Delete, Mandantenfilter, `[Realtime]`, Webhooks, Workflows und die Outbox bleiben beim Context der App. Zeilen eines externen Contexts haben keinen Mandanten, also entscheidet ein Zeilenfilter, wer was sieht, und eine externe Entity braucht ihr OData-Set, bevor Anhänge oder Kommentare ihre Zeilen öffnen dürfen.
- Eine Abfrage kann nicht zwei Datenbanken lesen. Ein Zeilenfilter auf einem externen Set, der von Daten der App abhängt (Gruppen, Kunden), lädt diese IDs zuerst und übergibt sie als Liste: `var codes = await db.Set<OrgCode>().Where(...).Select(c => c.Code).ToListAsync(); return query.Where(s => EF.Parameter(codes).Contains(s.OrgCode));`. Kind-Filter (`AddODataChildFilter`) brauchen Kind und Eltern in derselben Datenbank, und berechnete OData-Eigenschaften mit `ComputedQueryRoot` funktionieren nur auf dem Context der App; [berechnete Eigenschaften](../web/odata.md#berechnete-eigenschaften) über die eigenen Spalten der Zeile funktionieren auch auf externen Sets.
- Tags an externen Datensätzen vergleichen deren IDs als eine Liste, und Anhänge externer Datensätze werden nicht aufgeräumt, wenn der Datensatz verschwindet.
- Jedes Speichern erzeugt die registrierten externen Contexts, um nach Änderungen zu sehen; registriere also nur Contexts, die die App über Coworkee schreibt oder liest.
- Client-Entities brauchen die Datumsspalten der externen Tabelle (`ChangedAt` in den [Einstellungen](../ui/client-entities.md#schritt-2-einstellungen-optional)); ohne sie lädt ein Set bei jeder Aktualisierung vollständig. `Overlap` erhöhen, wenn ein anderes System die Daten mit eigener Uhr schreibt oder lange nach dem Stempeln committet.

## Migrationen und Seed

Migrationen liegen im Infrastruktur-Projekt und sind für dessen Datenbank-Provider erstellt:

```bash
dotnet ef migrations add AddOrders --project src/MyApp.Infrastructure
```

Ein kleiner Host spielt sie ein. Der Migrations-Dienst des Templates seedet außerdem:

```csharp title="MyApp.Migrations/Program.cs"
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<MyAppDatabaseModule>(builder.Configuration);
builder.AddCoworkeeMigrations<MyAppDbContext>(options =>
{
    options.DevelopmentSeed = DemoSeed.Configure;
    options.DeploymentSeed = DemoSeed.ConfigureDeployment;
});
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();
```

`AddCoworkeeMigrations` (`Coworkee.Identity.Setup`) plant den Lauf und `RunCoworkeeMigrationsAsync` führt ihn aus, mit Exit-Code 0 oder 1:

1. die Datenbank migrieren, alle 3 Sekunden erneut versucht, solange ihr Server noch keine Verbindungen annimmt (`Migrations:Attempts`, Standard 20, oder `options.Attempts`), und Gruppen ohne `Code`-Pfad bekommen ihren;
2. seeden: mit einem ersten Administrator aus den Deploy-Parametern (`Coworkee:InitialAdmin`, siehe [Setup-Assistent oder Seed](../security/authentication.md#setup-assistent-oder-seed)) `DeploymentSeed` mit ihm; sonst `DevelopmentSeed` in der Development-Umgebung (`options.DevelopmentUsers` entscheidet anders), danach `SyncDevelopmentUsersAsync`; sonst nichts, und die Setup-Seite mit ihrem Token richtet das System ein;
3. die [Seed-Contributors](#seed-contributors) der App und ihrer Module ausführen, auch wenn Schritt 2 nichts getan hat;
4. `options.AfterSeed` ausführen, falls gesetzt (besser: Seed-Contributors);
5. ein deploytes Keycloak abgleichen (`ReconcileKeycloakRealmAsync`);
6. die zwischengespeicherten Berechtigungen, Einstellungen und Features laufender Dienste verwerfen (mit Redis auf jeder Instanz), damit ein erneuter Lauf gegen eine laufende App oder ein rollendes Deployment sofort wirkt.

Außerdem baut es den Container mit den Produktions-Standards: Ein lokaler Lauf ist Development, und dessen Prüfung der Scopes scheitert an den MVC-Diensten von OData in einem Host ohne Web-Umgebung.

`AddCoworkeeIdentitySeed` ersetzt den Setup-Assistenten: Er legt einmalig den Standard-Mandanten, den Administrator, Rollen mit ihren Berechtigungen, weitere Benutzer und systemweite Einstellungen (`seed.Settings["Theme.Mode"] = "dark"`) an. `ISetupStep`-Implementierungen laufen im selben Setup (hier oder im Setup-Assistenten) als Administrator; für Daten der App sind Seed-Contributors der bessere Ort. `SyncDevelopmentUsersAsync` läuft nur in der Development-Umgebung (oder mit `Coworkee:Identity:SyncDevelopmentUsers`, das der Aspire-App-Host für die Migrationen eines lokalen Laufs setzt) und hält eine bestehende Datenbank im Stand des Seeds: Fehlende Benutzer und Rollen kommen dazu, Seed-Benutzer bekommen die seitdem im Seed ergänzten Rollen, Seed-Rollen die ergänzten Berechtigungen, Seed-Benutzer mit anderem Passwort bekommen wieder das geseedete; andere Benutzer, Rollen und Berechtigungen bleiben, wie sie sind, entfernt wird nichts. Siehe [Anmeldung](../security/authentication.md#setup-assistent-oder-seed).

`options.SyncRolesInDeployments = true` macht den Rollen-Teil davon für Deployments: Bei jedem Lauf kommen die Rollen von `DeploymentSeed` und ihre Berechtigungen in den Standard-Mandanten, auch in eine früher oder im Browser eingerichtete Datenbank (`DeploymentSeed` wird dann mit einem Platzhalter-Administrator aufgerufen, nur für seine Rollen). Fehlende Rollen und Berechtigungen kommen dazu, entfernt wird nichts, Benutzer und Passwörter bleiben unberührt. Standardmäßig aus; läuft vor den anderen Seed-Contributors.

### Seed-Contributors

Daten, die jede Datenbank braucht (Stammdaten, Tags, Einstellungen eines Moduls), und Änderungen an den geseedeten Konten gehören in einen `ISeedContributor` (`Coworkee.Application.Setup`), eine Klasse mit injizierten Diensten wie jede andere:

```csharp title="MyApp.Sales/Seeds/StandardTagsSeed.cs"
[RegisterAs(typeof(ISeedContributor))]
internal sealed class StandardTagsSeed(SalesDbContext db, IDispatcher dispatcher) : ISeedContributor
{
    private static readonly string[] Tags = ["Urgent", "Customs hold"];

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        var existing = await db.Tags.Select(t => t.Name).ToListAsync(cancellationToken);
        foreach (var name in Tags.Except(existing))
        {
            await dispatcher.SendAsync(new CreateTag(name), cancellationToken).OrThrow();
        }
    }
}
```

`coworkee seed add StandardTags --module Sales` schreibt so eine Klasse (siehe [CLI](../getting-started/cli.md)). `[RegisterAs]` wirkt in den Modulen und im Migrationsprojekt (`AddCoworkeeMigrations` durchsucht es mit); `services.AddTransient<ISeedContributor, StandardTagsSeed>()` geht überall.

| Member | Standard | |
|---|---|---|
| `SeedAsync(SeedContext, CancellationToken)` | | `context.TenantId` (null bei `Host`), `context.IsDevelopment`, `context.IsNew` |
| `Order` | 0 | kleiner läuft zuerst; bei Gleichstand nach Typname. Jeder Contributor läuft für alle Mandanten, bevor der nächste beginnt |
| `Environments` | `Always` | `Development` (ein Lauf, der die Demo-Benutzer seedet; auch ohne Development-Seed, wenn die Development-Umgebung oder `options.DevelopmentUsers` es so sagt) oder `Deployment` |
| `Target` | `EachTenant` | einmal je Mandant mit gesetztem Mandanten (auch inaktive), oder `Host`: einmal je Lauf ohne Mandant |
| `Required` | true | false: ein Fehler wird als Warnung geloggt und der Lauf geht weiter |

So läuft ein Aufruf: in einem eigenen DI-Scope und einer eigenen Datenbank-Transaktion, als das System mit allen Berechtigungen (`SystemActor`), im Mandanten des Aufrufs. Requests mit `[RequiresPermission]` gehen durch, `IMultiTenant`-Zeilen bekommen den Mandanten, das Audit-Protokoll zeigt "system", `CreatedBy` bleibt leer. `UserManager`, die Commands des Dispatchers und was der Contributor ungespeichert lässt, landen in der Transaktion; der Runner speichert am Ende. Ein Seed, der eine Person als Besitzer braucht, öffnet um diesen Teil `CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId))`. Das System ist in keiner Gruppe: Gruppen-IDs ausdrücklich übergeben oder mit `IgnoreQueryFilters()` lesen. Andere Datenbanken (externe DbContexts) und der Blob-Speicher sind nicht in der Transaktion; Schreibvorgänge dort müssen für sich idempotent sein.

**Jeder Lauf, also idempotent.** Ein Contributor läuft bei jedem Migrationslauf: lesen, was da ist, dann nur das Fehlende ergänzen oder ändern. Änderungen an einem Seed (ein neuer Tag, eine neue Berechtigung) erreichen jede Datenbank mit dem nächsten Lauf. Was Benutzern gehört (Beispieldatensätze), steht unter `if (!context.IsNew) return;`: `IsNew` ist wahr in dem Lauf, dessen Identity-Seed das System eingerichtet hat, und für einen gerade eingerichteten Mandanten; solche Datensätze kommen nach dem Löschen also nicht zurück. Welche Seeds gelaufen sind, wird nicht festgehalten.

**Fehler.** Ein fehlschlagender Pflicht-Contributor rollt seine Transaktion zurück, das Log nennt Contributor und Mandant, und der Lauf endet mit Exit-Code 1, damit die Dienste eines Deployments nicht gegen eine halb befüllte Datenbank starten. Spätere Contributors laufen nicht, weil sie auf ihm aufbauen können.

**Wo sie laufen.**

| Contributor | Migrationslauf (jeder Lauf) | Setup-Assistent, neuer Mandant (`POST /api/v1/tenants`) |
|---|---|---|
| in einem Modul, `EachTenant` | für jeden Mandanten | in der API, für den neuen Mandanten, im Hintergrund nach dem Commit (`IsNew` wahr) |
| in einem Modul, `Host` | einmal | nein |
| im Migrationsprojekt | ja | nein, die API lädt dieses Projekt nicht |
| `SyncRolesInDeployments` | Deployments | nein |

Für einen zur Laufzeit eingerichteten Mandanten startet die API sie über die Outbox (`TenantSeedRequested`, auch über `tenant.RequestSeed()`): Der Outbox-Prozessor der API führt sie aus, sobald das Setup oder der neue Mandant committet ist, mit `IsDevelopment` aus der Development-Umgebung der API. Ein Fehler dort wird als Fehler geloggt und nicht wiederholt (eine Wiederholung würde wiederholen, was `IsNew` schützt); der nächste Migrationslauf (ein Deployment, *Re-run migrations* im Aspire-Dashboard, `coworkee db seed`) ist der Rückfall für alles außer `IsNew`. Contributors im Migrationsprojekt erreichen solche Mandanten erst mit diesem nächsten Lauf.

Schreibvorgänge eines Seeds senden Realtime-Nachrichten und Outbox-Events wie jedes Speichern: Der Outbox-Prozessor der API stellt sie zu, sobald sie läuft. Zwei Migrationsläufe gleichzeitig (eine überlappende Wiederholung des Jobs) führen jeden Seed zweimal aus; idempotente Seeds und eindeutige Indizes machen den zweiten zu einem No-op oder einem zurückgerollten Fehler.

**Testen.** `services.RunSeedContributorAsync<T>(new SeedContext(tenantId, IsDevelopment: true, IsNew: true))` führt einen Contributor so aus wie die Migrationen, auch einen, den der Host nicht registriert (siehe [Testen](../testing.md)).
