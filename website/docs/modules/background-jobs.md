# Background jobs

Jobs run on Hangfire with the storage of the [database provider](../fundamentals/persistence.md#database-providers): Postgres, SQL Server, or in memory with SQLite. A job runs as the user who enqueued it, so permissions, tenant filters and audit work as in a request.

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

Work in every organisation goes through `TenantJobRunner`: once per active organisation, as the system of that organisation (tenant filters, audit) and in a DI scope of its own. A failing organisation is logged and returned, the others still run:

```csharp
internal sealed class ReminderJob(TenantJobRunner tenants) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken) =>
        await tenants.ForEachTenantAsync((services, tenantId, ct) =>
            services.GetRequiredService<IDispatcher>().SendAsync(new SendReminders(), ct), cancellationToken);
}
```

`ForTenantsAsync(tenantIds, ...)` does the same for the organisations a query found (due rows grouped by `TenantId`), so organisations without work cost nothing. `ForEachTenantAsync` lists the organisations through the Identity module. The run is sequential; with many organisations keep the work per organisation short.

| `Coworkee:Jobs` | |
|---|---|
| `RunServer` | process jobs in this host (the app host turns it off for the auth server) |
| `Queues` | queues this host works on, default `default`, `mail` |
| `WorkerCount` | parallel workers (5) |
| `Attempts`, `RetryDelaysInSeconds` | retries |

The Hangfire dashboard is under `/admin/jobs` for users with the jobs permission; the admin page **Services** shows it as a tile. Never block a job waiting for another one; schedule a follow-up instead.
