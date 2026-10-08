# Persistenz

Die App hat genau einen `DbContext`, abgeleitet von `CoworkeeDbContext`. Module tragen ihre Entities bei; Coworkee bringt die Tabellen für Identity, Einstellungen, Audit und Outbox mit.

```csharp title="MyApp.Infrastructure/MyAppDbContext.cs"
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
```

```csharp title="MyApp.Infrastructure/MyAppInfrastructureModule.cs"
context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) => options.UseNpgsql(
    provider.GetRequiredService<IConfiguration>().GetConnectionString("myapp"),
    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "cw")));
```

Handler injizieren `CoworkeeDbContext` und arbeiten mit `db.Set<T>()`. Repositories gibt es nicht; EF Core ist bereits eines.

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

Migrationen liegen im Infrastruktur-Projekt:

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
```

`AddCoworkeeIdentitySeed` ersetzt den Setup-Assistenten: Er legt einmalig den Standard-Mandanten, den Administrator, Rollen mit ihren Berechtigungen und weitere Benutzer an. `ISetupStep`-Implementierungen laufen im selben Setup als Administrator und sind der Ort für Beispieldaten. Siehe [Anmeldung](../security/authentication.md#setup-assistent-oder-seed).
