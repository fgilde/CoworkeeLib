# Background jobs

Jobs run on Hangfire with Postgres storage. A job runs as the user who enqueued it, so permissions, tenant filters and audit work as in a request.

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

Recurring jobs run outside of a user and tenant:

```csharp
internal sealed class CleanupJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

| `Coworkee:Jobs` | |
|---|---|
| `RunServer` | process jobs in this host (the app host turns it off for the auth server) |
| `Queues` | queues this host works on, default `default`, `mail` |
| `WorkerCount` | parallel workers (5) |
| `Attempts`, `RetryDelaysInSeconds` | retries |

The Hangfire dashboard is under `/admin/jobs` for users with the jobs permission; the admin page **Services** shows it as a tile. Never block a job waiting for another one; schedule a follow-up instead.
