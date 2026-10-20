# Hintergrundjobs

Jobs laufen auf Hangfire mit dem Speicher des [Datenbank-Providers](../fundamentals/persistence.md#datenbank-provider): Postgres, SQL Server oder bei SQLite im Arbeitsspeicher. Ein Job läuft als der Benutzer, der ihn eingereiht hat; Berechtigungen, Mandantenfilter und Audit funktionieren wie in einem Request.

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

Wiederkehrende Jobs laufen ohne Benutzer und Mandant:

```csharp
internal sealed class CleanupJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

Arbeit in jeder Organisation läuft über `TenantJobRunner`: einmal pro aktiver Organisation, als System dieser Organisation (Mandantenfilter, Audit) und in einem eigenen DI-Scope. Eine fehlschlagende Organisation wird protokolliert und zurückgegeben, die anderen laufen weiter:

```csharp
internal sealed class ReminderJob(TenantJobRunner tenants) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken) =>
        await tenants.ForEachTenantAsync((services, tenantId, ct) =>
            services.GetRequiredService<IDispatcher>().SendAsync(new SendReminders(), ct), cancellationToken);
}
```

`ForTenantsAsync(tenantIds, ...)` macht dasselbe für die Organisationen, die eine Abfrage gefunden hat (fällige Zeilen nach `TenantId` gruppiert), so kosten Organisationen ohne Arbeit nichts. `ForEachTenantAsync` holt die Organisationen über das Identity-Modul. Der Lauf ist sequenziell; bei vielen Organisationen die Arbeit pro Organisation kurz halten.

| `Coworkee:Jobs` | |
|---|---|
| `RunServer` | Jobs in diesem Host verarbeiten (der AppHost schaltet das für den Auth-Server ab) |
| `Queues` | Warteschlangen dieses Hosts, Standard `default`, `mail` |
| `WorkerCount` | parallele Worker (5) |
| `Attempts`, `RetryDelaysInSeconds` | Wiederholungen |

Das Hangfire-Dashboard liegt unter `/admin/jobs`, für Benutzer mit der Job-Berechtigung; die Admin-Seite **Dienste** zeigt es als Kachel. Blockiere nie einen Job, um auf einen anderen zu warten; plane stattdessen einen Folgejob ein.
