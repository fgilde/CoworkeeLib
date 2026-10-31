# Database backups

`Coworkee.Backup` saves the whole database as one zip in the [file storage](storage.md) and restores it. It needs Postgres; on SQL Server use its own backups, on SQLite copy the file.

![Backups page](../assets/screenshots/backups.png){ .shot }

```csharp
[DependsOn(typeof(CoworkeeBackupModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

The module depends on the storage module. The template adds it with the option `backup` (on by default) when the database is Postgres. For a local database outside the app, `coworkee db backup` and `coworkee db restore` of the [CLI](../getting-started/cli.md) work with every database.

- Every table of the model is written with Postgres `COPY ... (FORMAT BINARY)`; a manifest records the tables and the last applied migration.
- A restore replaces every table in one transaction. It refuses a backup made at another migration or with other tables.
- A downloaded zip can be uploaded again (also into another installation): it shows up in the list and is restored like any other backup. An upload without a manifest is refused; whether it fits the database is checked at the restore.
- Caches are cleared after a restore.
- Backups hold every organisation, so only users of the system organisation with `Backups.Manage` see them; other organisations get `403` (`backup.system_only`). The admin page is *Administration > System > Backups* (`/cw/admin/backups`).
- A restore onto another migration or other tables answers `409` (`backup.mismatch`); an uploaded file name needs 1 to 200 characters, the upload itself has no size limit.
- With the [Retention](retention.md) module the target `backups` deletes old backups with their zip; the newest backup always stays.

| Endpoint | |
|---|---|
| `GET /api/v1/backups` | list |
| `POST /api/v1/backups` | create |
| `GET /api/v1/backups/{id}/download` | zip |
| `POST /api/v1/backups/upload?name=` | upload a zip (the body is the file) |
| `POST /api/v1/backups/{id}/restore` | restore |
| `DELETE /api/v1/backups/{id}` | delete |

!!! warning "Database rights"
    The restore loads tables with `session_replication_role = replica`, which skips foreign key checks while tables load in any order. The database user needs the right to set it (superuser, as in the Aspire setup, or a role with that right).

The table `cw.DatabaseBackups` itself is not part of a backup, so the list survives a restore.
