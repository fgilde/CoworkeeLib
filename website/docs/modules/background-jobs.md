# Background jobs

`Coworkee.BackgroundJobs` (`CoworkeeBackgroundJobsModule`) runs jobs on Hangfire with the storage of the [database provider](../fundamentals/persistence.md#database-providers): Postgres or SQL Server (schema `hangfire`), or in memory with SQLite. Mailing, OData imports and exports, chat, notifications, retention and other modules depend on it.

## Jobs of a user

A job runs as the user who enqueued it, in the user's organisation and in a DI scope of its own, so permissions, tenant filters and audit work as in a request. Someone signed in as another user (*Log in as*) stays on record: the job keeps the impersonator (`JobEnvelope.ImpersonatorId`), and its audit entries name him like those of the request.

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

`IBackgroundJobs` is scoped; the job class needs no registration, it is created in the job's scope. The arguments are serialized into the job storage: keep them small (ids instead of entities) and free of secrets. A queue other than `default` and `mail` needs a host that lists it in `Coworkee:Jobs:Queues`, otherwise its jobs wait forever.

## Recurring jobs

Recurring jobs run outside of a user and tenant:

```csharp
internal sealed class CleanupJob : IRecurringJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

The schedule (cron, UTC) is written to the job storage at start; while the database is not reachable yet, the host retries every five seconds. Hangfire starts one run per due time across all instances.

## Every organisation

Work in every organisation goes through `TenantJobRunner`: once per active organisation, as the system of that organisation (tenant filters, audit) and in a DI scope of its own. A failing organisation is logged and returned, the others still run:

```csharp
internal sealed class ReminderJob(TenantJobRunner tenants) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken) =>
        await tenants.ForEachTenantAsync((services, tenantId, ct) =>
            services.GetRequiredService<IDispatcher>().SendAsync(new SendReminders(), ct), cancellationToken);
}
```

`ForTenantsAsync(tenantIds, ...)` does the same for the organisations a query found (due rows grouped by `TenantId`), so organisations without work cost nothing. `ForEachTenantAsync` lists the organisations through the Identity module (`ITenantDirectory.GetActiveTenantIdsAsync`). The run is sequential; with many organisations keep the work per organisation short.

## Only one instance at a time

`IDistributedLock` (`Coworkee.Application`) is a lock held across all instances of the app, for work that must not run twice at once:

```csharp
internal sealed class DigestJob(IDistributedLock locks) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var held = await locks.AcquireAsync("my-digest", TimeSpan.Zero, cancellationToken);
        if (held is null)
        {
            return;   // another instance is at it
        }

        // ...
    }
}
```

`AcquireAsync(name, timeout)` waits up to `timeout` and returns `null` when another holder kept the lock that long; disposing the handle releases it. By default the database holds it (a Postgres advisory lock, a SQL Server application lock, on SQLite a file lock in the temp folder for the processes of one machine); a crashed holder's connection releases it. With Redis configured, `Coworkee:DistributedLock:Provider` = `Redis` uses a Redis key with a short expiry that the holder keeps extending. The notification digest and mails, scheduled exports and the first machine translation use it.

## Configuration

| `Coworkee:Jobs` | |
|---|---|
| `ConnectionStringName` | the connection string of the job storage (`default`; the app host sets the app database) |
| `RunServer` | process jobs in this host (`true`; the app host turns it off for the auth server). With SQLite every host processes its own jobs, whatever the value |
| `Queues` | queues this host works on, default `default`, `mail` |
| `WorkerCount` | parallel workers (5) |
| `Attempts` | attempts of a failing job in total (3) |
| `RetryDelaysInSeconds` | the waits between them (10, 60, 300) |
| `PollingInterval` | how often the server looks for queued and scheduled jobs (15 seconds) |
| `DashboardPath` | address of the Hangfire dashboard (`/admin/jobs`) |

Only hosts that process jobs create the `hangfire` schema. SQLite keeps jobs in memory: queued and scheduled jobs are lost on a restart, fine for development only.

## Dashboard and status

The Hangfire dashboard is under `/admin/jobs` (`DashboardPath`; not under the `/cw` prefix of the library pages) for users with the permission `Jobs.View`; the admin page **Services** shows it as a tile. The [system status](../features/blazor-ui.md#system-status) has a section *Background jobs* with the last failures.

Never block a job waiting for another one; schedule a follow-up instead.
