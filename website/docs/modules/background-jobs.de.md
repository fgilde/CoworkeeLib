# Hintergrundjobs

`Coworkee.BackgroundJobs` (`CoworkeeBackgroundJobsModule`) führt Jobs auf Hangfire mit dem Speicher des [Datenbank-Providers](../fundamentals/persistence.md#datenbank-provider) aus: Postgres oder SQL Server (Schema `hangfire`) oder bei SQLite im Arbeitsspeicher. Mailversand, OData-Importe und -Exporte, Chat, Benachrichtigungen, Aufbewahrung und weitere Module hängen davon ab.

## Jobs eines Benutzers

Ein Job läuft als der Benutzer, der ihn eingereiht hat, in dessen Organisation und in einem eigenen DI-Scope; Berechtigungen, Mandantenfilter und Audit funktionieren wie in einem Request. Wer als ein anderer Benutzer angemeldet ist (*Anmelden als*), bleibt festgehalten: Der Job behält den Impersonator (`JobEnvelope.ImpersonatorId`), und seine Audit-Einträge nennen ihn wie die des Requests.

```csharp
public sealed record RenderPreview(Guid AssetId);

internal sealed class RenderPreviewJob(IDispatcher dispatcher) : IBackgroundJob<RenderPreview>
{
    public Task ExecuteAsync(RenderPreview args, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new CreatePreviewCommand(args.AssetId), cancellationToken);
}

jobs.Enqueue<RenderPreviewJob, RenderPreview>(new RenderPreview(asset.Id), queue: "processing");
jobs.Schedule<RenderPreviewJob, RenderPreview>(new RenderPreview(asset.Id), TimeSpan.FromMinutes(5));
```

`IBackgroundJobs` ist scoped; die Job-Klasse braucht keine Registrierung, sie wird im Scope des Jobs erzeugt. Die Argumente werden in den Job-Speicher serialisiert: Halte sie klein (IDs statt Entitäten) und frei von Geheimnissen. Eine Queue außer `default` und `mail` braucht einen Host, der sie in `Coworkee:Jobs:Queues` aufführt, sonst warten ihre Jobs für immer.

## Wiederkehrende Jobs

Wiederkehrende Jobs laufen ohne Benutzer und Mandant:

```csharp
internal sealed class CleanupJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

Der Zeitplan (Cron, UTC) wird beim Start in den Job-Speicher geschrieben; solange die Datenbank noch nicht erreichbar ist, versucht es der Host alle fünf Sekunden erneut. Hangfire startet pro fälligem Zeitpunkt einen Lauf über alle Instanzen.

## Jede Organisation

Arbeit in jeder Organisation läuft über `TenantJobRunner`: einmal pro aktiver Organisation, als System dieser Organisation (Mandantenfilter, Audit) und in einem eigenen DI-Scope. Eine fehlschlagende Organisation wird protokolliert und zurückgegeben, die anderen laufen weiter:

```csharp
internal sealed class ReminderJob(TenantJobRunner tenants) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken) =>
        await tenants.ForEachTenantAsync((services, tenantId, ct) =>
            services.GetRequiredService<IDispatcher>().SendAsync(new SendReminders(), ct), cancellationToken);
}
```

`ForTenantsAsync(tenantIds, ...)` macht dasselbe für die Organisationen, die eine Abfrage gefunden hat (fällige Zeilen nach `TenantId` gruppiert), so kosten Organisationen ohne Arbeit nichts. `ForEachTenantAsync` holt die Organisationen über das Identity-Modul (`ITenantDirectory.GetActiveTenantIdsAsync`). Der Lauf ist sequenziell; bei vielen Organisationen die Arbeit pro Organisation kurz halten.

## Nur eine Instanz gleichzeitig

`IDistributedLock` (`Coworkee.Application`) ist eine Sperre über alle Instanzen der App, für Arbeit, die nicht zweimal gleichzeitig laufen darf:

```csharp
internal sealed class DigestJob(IDistributedLock locks) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync("my-digest", TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;   // eine andere Instanz ist dran
        }

        // ...
    }
}
```

`AcquireAsync(name, timeout)` wartet bis zu `timeout` und gibt `null` zurück, wenn ein anderer Halter die Sperre so lange behalten hat; das Freigeben des Handles löst sie. Standardmäßig hält die Datenbank sie (ein Advisory Lock unter Postgres, ein Application Lock unter SQL Server, unter SQLite eine Dateisperre im Temp-Ordner für die Prozesse eines Rechners); die Verbindung eines abgestürzten Halters gibt sie frei. Mit Redis nimmt `Coworkee:DistributedLock:Provider` = `Redis` einen Redis-Schlüssel mit kurzer Ablaufzeit, die der Halter laufend verlängert. Die Benachrichtigungs-Zusammenfassung und -Mails, geplante Exporte und die erste maschinelle Übersetzung nutzen sie.

## Konfiguration

| `Coworkee:Jobs` | |
|---|---|
| `ConnectionStringName` | der Connection String des Job-Speichers (`default`; der AppHost setzt die App-Datenbank) |
| `RunServer` | Jobs in diesem Host verarbeiten (`true`; der AppHost schaltet das für den Auth-Server ab). Mit SQLite verarbeitet jeder Host seine eigenen Jobs, unabhängig vom Wert |
| `Queues` | Warteschlangen dieses Hosts, Standard `default`, `mail` |
| `WorkerCount` | parallele Worker (5) |
| `Attempts` | Versuche eines fehlschlagenden Jobs insgesamt (3) |
| `RetryDelaysInSeconds` | die Wartezeiten dazwischen (10, 60, 300) |
| `PollingInterval` | wie oft der Server nach eingereihten und geplanten Jobs schaut (15 Sekunden) |
| `DashboardPath` | Adresse des Hangfire-Dashboards (`/admin/jobs`) |

Nur Hosts, die Jobs verarbeiten, legen das Schema `hangfire` an. SQLite hält Jobs im Arbeitsspeicher: Eingereihte und geplante Jobs gehen bei einem Neustart verloren, nur für die Entwicklung geeignet.

## Dashboard und Status

Das Hangfire-Dashboard liegt unter `/admin/jobs` (`DashboardPath`; nicht unter dem Präfix `/cw` der Bibliotheksseiten), für Benutzer mit der Berechtigung `Jobs.View`; die Admin-Seite **Dienste** zeigt es als Kachel. Der [Systemstatus](../features/blazor-ui.md#systemstatus) hat einen Abschnitt *Background jobs* mit den letzten Fehlern.

Blockiere nie einen Job, um auf einen anderen zu warten; plane stattdessen einen Folgejob ein.
