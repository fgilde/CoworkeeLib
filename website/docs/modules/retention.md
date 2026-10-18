# Retention

`Coworkee.Retention` decides how long data is kept. Modules report the kinds of data that grow without end (audit trail, outbox, logs, deleted records, files, tokens), a nightly job deletes what is older than the days set for each, and the admin page *Retention* (`/admin/retention`) sets the days and deletes on demand.

```csharp
[DependsOn(typeof(CoworkeeRetentionModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

The module brings the table `cw.RetentionRuns` (last run per kind of data): **add a migration** when you add it to an existing app.

## Kinds of data

Every kind of data is deleted across all organisations. The library never deletes on its own after an update: everything that was kept forever before is kept forever until someone sets days. Only the webhook delivery log keeps its 30 days.

| Key | Module | Data | Age by | Default |
|---|---|---|---|---|
| `audit` | Retention | audit trail (`cw.AuditEntries`) | `OccurredAt` | never |
| `snapshots` | Retention | revisions of versioned entities (`cw.EntitySnapshots`) | `CreatedAt` | never |
| `outbox` | Retention | processed outbox messages, pending ones stay | `ProcessedAt` | never |
| `soft-deleted` | Retention | every soft deleted type of the model, except files | `DeletedAt` | never |
| `inbox` | EventBus | processed integration events (`cw.InboxMessages`) | `ProcessedAt` | never |
| `ai-tool-calls` | Ai | tool call audit | `At` | never |
| `ai-sessions` | Ai | assistant conversations with their messages | `UpdatedAt` | never |
| `webhook-deliveries` | Webhooks | delivery log without pending deliveries | `CreatedAt` | `LogRetentionDays` (30) |
| `mail-log` | Mailing | sent, failed and skipped mails | `QueuedAt` | never |
| `notifications` | Notifications | in-app notifications | `CreatedAt` | never |
| `data-transfer-jobs` | OData | finished imports and exports with their files | `FinishedAt` | never |
| `deleted-files` | Files | soft deleted files with their blobs, then empty soft deleted folders | `DeletedAt` | never |
| `backups` | Backup | database backups with their zip | `CreatedAt` | never |
| `auth-tokens` | AuthServer | what OpenIddict's prune removes: revoked, redeemed or expired tokens, invalid authorizations and ad-hoc ones without tokens | creation | never |

- `audit`: entity history and restore read the audit trail. Older changes can no longer be shown or undone.
- `soft-deleted`: one type after the other. A type that a foreign key still holds (a soft deleted owner whose children stay) is reported as error of the run and the next type is tried.
- `data-transfer-jobs`: an import whose job is gone can no longer be undone.

New apps of the template start with technical defaults in `src/MyApp.Api/appsettings.json`: `outbox`, `inbox` and `auth-tokens` 14 days, `data-transfer-jobs` 30 days.

## Settings

Each kind of data has the Global setting `Retention.{key}.Days` (empty or `0`: keep everything). The settings live on the admin page *Retention*, not in the settings tabs. The default comes from the module or from the configuration:

```json
{
  "Coworkee": {
    "Settings": {
      "Defaults": {
        "Retention.audit.Days": "365",
        "Retention.mail-log.Days": "90"
      }
    }
  }
}
```

The webhook module still reads `Coworkee:Webhooks:LogRetentionDays` as default of `Retention.webhook-deliveries.Days`. Hosts without the Retention module keep the daily webhook log cleanup of before; with the module it does nothing and the nightly retention job deletes.

## The admin page

*Monitoring → Retention*, for users of the system organisation with `Retention.Manage` (administrators get it like every other permission). One row per kind of data:

- **Keep (days)**: empty keeps everything; while typing, *Affected now* counts what these days would delete. *Save* stores every changed row.
- **Last run**: when, how many rows, and the error of the last run. A row that is running shows a progress bar; the page asks every 2 seconds until it is done.
- **Delete now** asks with the number of rows (*Delete 12,480 entries older than 365 days?*) and runs in a background job. The log and the table remember who started it.

The section *Retention* of the system status page (`/admin/system-status`) shows the last run and the errors of every kind of data.

| Endpoint | |
|---|---|
| `GET /api/v1/retention` | kinds of data with days, default, affected rows, last run |
| `GET /api/v1/retention/{key}/count?days=N` | rows older than `N` days |
| `PUT /api/v1/retention` | `{ "audit": 365, "mail-log": null }` stores the days |
| `POST /api/v1/retention/{key}/run` | body `{ "days": N }` (at least 1): `202` and a background job, `409` while the key runs |

All of them need `Retention.Manage` and the system organisation (`403` with `retention.system_only` otherwise).

## Your own kind of data

Implement `IRetentionTarget` and register it; it appears on the page and in the nightly run.

```csharp
internal sealed class SensorReadingRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "sensor-readings";

    public string Title => "Sensor readings";   // English text, also the localization key

    public string? Description => "Raw readings of the devices; the daily sums stay.";

    public int? DefaultDays => null;            // null: keep everything until set

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(r => r.At).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<SensorReading> Older(DateTimeOffset before) =>
        db.Set<SensorReading>().IgnoreQueryFilters().Where(r => r.At < before);
}

services.AddRetentionTarget<SensorReadingRetention>();
```

- Delete across all organisations: `IgnoreQueryFilters()`.
- `ExecuteDeleteInBatchesAsync` (`Coworkee.Infrastructure.Persistence`) deletes 1000 rows per statement, so no statement locks a large table for long. Like every `ExecuteDelete` it skips the change tracker: no audit entry, no realtime message.
- Rows with a blob: delete the row first, then the blob.
- A soft deleted type that your target cleans up itself is left out of `soft-deleted` with `services.AddRetentionOwnedType<T>()`.
- A target that deleted some rows but could not delete others throws `RetentionIncompleteException(deleted, message)`; the run records both.

## Job and limits

`coworkee-retention` runs at `Coworkee:Retention:Cron` (default `41 2 * * *`, UTC). It runs the kinds of data with days set one after the other, each in its own scope; an error is written to the run and does not stop the others. A run that crashed with its process is given up after two hours.

- Days are system wide: there are no own days per organisation.
- There is no archive: deleted data is gone.
- The job history of Hangfire and the instances of Elsa have their own expiry and are not on the page; unsent chat attachments keep their hourly cleanup.
