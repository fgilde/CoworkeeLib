# Hintergrundjobs

Jobs laufen auf Hangfire mit Postgres als Speicher. Ein Job läuft als der Benutzer, der ihn eingereiht hat; Berechtigungen, Mandantenfilter und Audit funktionieren wie in einem Request.

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

| `Coworkee:Jobs` | |
|---|---|
| `RunServer` | Jobs in diesem Host verarbeiten (der AppHost schaltet das für den Auth-Server ab) |
| `Queues` | Warteschlangen dieses Hosts, Standard `default`, `mail` |
| `WorkerCount` | parallele Worker (5) |
| `Attempts`, `RetryDelaysInSeconds` | Wiederholungen |

Das Hangfire-Dashboard liegt unter `/admin/jobs`, für Benutzer mit der Job-Berechtigung. Blockieren Sie nie einen Job, um auf einen anderen zu warten; planen Sie stattdessen einen Folgejob ein.
