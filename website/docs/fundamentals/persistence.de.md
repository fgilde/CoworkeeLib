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
- Facetten über `bool`-Spalten (`[ProvideFacet]` auf `IsActive`) scheitern auf SQL Server und SQLite, die den Wert als `1`/`0` zurückgeben; behoben im nächsten Release von Nextended.
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

## Mandanten und aktueller Benutzer

`ICurrentUser` trägt Benutzer-ID, Mandanten-ID und Berechtigungen des Requests. Hintergrundjobs und die Outbox stellen den Benutzer wieder her, der sie gestartet hat. Wer als jemand anderes handeln muss, etwa in einem Seed, öffnet einen Scope:

```csharp
using (CurrentUserScope.Begin(new ImpersonatedUser(adminId, tenantId)))
{
    // Abfragen und Anlegen laufen als dieser Benutzer und Mandant
}
```

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
builder.Services.AddCoworkeeIdentitySeed(DemoSeed.Configure);
builder.Services.AddScoped<ISetupStep, DemoData>();
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
await host.Services.SeedCoworkeeIdentityAsync();
await host.Services.SyncDevelopmentUsersAsync();
```

`AddCoworkeeIdentitySeed` ersetzt den Setup-Assistenten: Er legt einmalig den Standard-Mandanten, den Administrator, Rollen mit ihren Berechtigungen, weitere Benutzer und systemweite Einstellungen (`seed.Settings["Theme.Mode"] = "dark"`) an. `ISetupStep`-Implementierungen laufen im selben Setup als Administrator und sind der Ort für Beispieldaten. `SyncDevelopmentUsersAsync` läuft nur in der Development-Umgebung (oder mit `Coworkee:Identity:SyncDevelopmentUsers`, das der Aspire-App-Host für die Migrationen eines lokalen Laufs setzt) und hält eine bestehende Datenbank im Stand des Seeds: Fehlende Benutzer und Rollen kommen dazu, Seed-Rollen bekommen die seitdem im Seed ergänzten Berechtigungen, Seed-Benutzer mit anderem Passwort bekommen wieder das geseedete; andere Benutzer, Rollen und Berechtigungen bleiben, wie sie sind, entfernt wird nichts. Siehe [Anmeldung](../security/authentication.md#setup-assistent-oder-seed).
