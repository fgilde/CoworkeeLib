# Import and export

Users bring rows in from CSV, Excel or JSON files and take them out again, without you writing a parser or an upload page. Every imported row runs through your own command, so permissions, the organisation, validation and the audit log apply exactly as when someone fills in the form.

## Opt in per entity set

An entity set can be imported once you register how a row becomes a command. Sets without a registration stay export-only.

```csharp
// create and update: the id is null for new rows and the found row's id for updates
services.AddODataImport("Brands",
    (Guid? id, AddEditBrandRequest row) => new AddEditBrandCommand(id, row),
    id => new DeleteBrandsCommand([id]));   // optional: makes the last import undoable

// create only
services.AddODataImport("Notes", (AddNoteRequest row) => new AddNoteCommand(row));
services.AddODataImport<AddNoteCommand>("Notes");   // the command itself is the row
```

The row type's public properties with simple types (text, numbers, dates, `Guid`, `bool`, enums) are the fields a column can be mapped to. A field's label comes from `[Display]`, `[DisplayName]` or `[Description]`, otherwise from its name ("MadeAt" reads "Made at"). `[Required]` and `required` mark fields the wizard asks for.

A `Guid` property such as `BrandId` next to a navigation `Brand` of another OData set is a reference: the file may contain the brand's name instead of its id. The name comes from the first of `Name`, `Title`, `DisplayName`, `Code` and `Key`, otherwise the first text property.

## The wizard

`CoworkeeDataTable` shows an *Import* button with `Importable="true"`. It opens a side sheet in four steps:

1. **File**: CSV, TSV, Excel (`.xlsx`) or JSON up to 20 MB. For text files the wizard detects encoding (byte order mark, otherwise UTF-8 or Windows-1252), separator and whether the first row is a header, and you can correct each of them.
2. **Columns**: every column gets a suggested field, matched by name, label or the label in the user's language. You choose *add new rows*, *update existing rows* or *update or add*; the last two need a key column, for example the id or the name. For text files you set the language of numbers and dates (`1.234,50` and `15.01.2026` in `de-DE`). A mapping can be saved under a name per entity set and loaded again for the next file of the same supplier.
3. **Check**: the first 200 rows run through conversion, key lookup and the FluentValidation validators of the commands, without writing anything. You see what each row would do and why a row fails.
4. **Import**: a *dry run* checks every row in the background, then *Import* writes them. Both run as a background job; the progress arrives live over the user's realtime topic. Failed rows can be downloaded as CSV with an extra *Error* column, fixed and imported again. *Undo import* deletes the rows the user's last import of that set created (updated rows keep their new values).

Updates start from the stored row: columns you did not map keep their values.

Values are read like this:

| Type | Accepted |
|---|---|
| Numbers | in the chosen language for text files, invariant for Excel and JSON; whole numbers refuse fractions |
| Dates | ISO first (`2026-01-15`), then the chosen language |
| Booleans | `true/false`, `yes/no`, `y/n`, `1/0`, `x`, `on/off` and *yes*/*no* in the user's language |
| Enums | name, number, label (`[Display]`/`[Description]`) or its translation |
| References | id or display text, only rows the user may read |

## Admin page

*Administration → System → Import and export* (`/admin/data-transfer`, permission `DataTransfer.Use`) lists every set the user may import, exports whole sets and shows the user's imports and exports with their result files and the undo.

## Export

The export menu of a table writes the current view: search, facets, column filters, sort and the visible columns. Excel, CSV and JSON come from the server:

```http
GET /api/v1/data/Products/export?format=csv&columns=Name,Rate&$filter=Rate gt 10&$orderby=Name
```

Up to `Coworkee:OData:BackgroundExportRows` rows (default 10 000) the file comes right back. Larger exports answer `202 Accepted` with a job; the user gets a notice with a *Download* button when the file is ready. `Coworkee:OData:MaxExportRows` (default 100 000) caps every export. Permissions, entity filters and hidden properties apply as in the list.

### Progress stream and one-time download

`GET /api/v1/data/{set}/export/stream` takes the same query and answers `application/x-ndjson`, one `DataExportProgress` per line: `counting`, `reading` (0-80 %, every 500 rows), `writing`, then `ready` with `url` (`api/v1/data/downloads/{token}`, relative to the API), `fileName` and `jobId`, or `failed` with a message. The link needs no sign-in, so a native `<a download>` works; it opens the file once and only for five minutes (`IAccessTokens`, see [files](../modules/files.md#temporary-links)). The export runs without the request's cancellation: when the client leaves before `ready`, it is written all the same and mailed to the user. Either way the user's job list keeps the file. `IDataTransferApi.StreamExportAsync` reads the lines in the Blazor client.

More rows than `BackgroundExportRows` end the stream with `queued` and the `jobId`: the export job writes the file, as for any background export (and mails it when the client had left). A user has at most `Coworkee:OData:MaxConcurrentExports` (default 2) exports running or queued; another stream answers `429`. A streamed export is cancelled after `Coworkee:OData:ExportTimeout` (default 30 minutes) and its job fails; running or queued exports older than that (a server stopped while writing them) fail when the user starts the next one, so none stays running for ever.

### Mailed and scheduled exports

`POST /api/v1/data/{set}/export/mail` with `MailExportRequest(Query, Recipients, FileName)` writes the export as a background job and mails it to the user and the recipients (`202` with the job). Recipients other than the user need `DataTransfer.ExportSchedules`.

Scheduled exports (`DataTransfer.ExportSchedules` plus the set's own permission) mail a list on a schedule: `ExportSchedule` (table `cw.ExportSchedules`) keeps the set, the query string verbatim (`$filter`, `$search`, `$orderby`, `columns`, `format`), a name, a file name, further recipients, the selected groups and a `ReportSchedule`: daily, weekly on a weekday or monthly on a day or on the first to fourth or last weekday (`Week`), every n-th period (`Every`), at a time of a time zone. The job `coworkee-export-schedules` (every five minutes) runs each due schedule as its owner, so the owner's permissions, row filters and groups decide what the file holds; each run is a job of the owner with the file, and errors stay on the schedule (`LastError`). A run sends nothing once the owner is deactivated or has lost `DataTransfer.ExportSchedules` or the set's permission; the reason is the schedule's `LastError`. A schedule or background export saved with selected groups lists only those the owner still has access to; when none is left it fails instead of listing every group.

| Call | Purpose |
|---|---|
| `GET /api/v1/data/export-schedules?entitySet=` | the user's schedules |
| `POST /api/v1/data/export-schedules`, `PUT .../{id}` | create or change one (`SaveExportScheduleRequest`) |
| `DELETE /api/v1/data/export-schedules/{id}` | delete one |
| `POST /api/v1/data/export-schedules/{id}/run` | send it now; the schedule stays |

The export menu of a table offers *Schedule export* to holders of the permission; the page *Scheduled exports* (`/data/export-schedules`, key `export-schedules`) lists them with *Send now*, edit and delete. The mail is the template `Data.Export` (Mailing module, 23 languages); files above 10 MB are not attached, the mail then points to the app. Without the Mailing module nothing is mailed (`IExportMailer`). App values such as a currency stay in the query string; the app reads them itself.

## HTTP API

| Call | Purpose |
|---|---|
| `GET /api/v1/data/imports` | importable sets with their fields |
| `POST /api/v1/data/{set}/import/file` | upload (multipart, field `file`), answers the detected columns |
| `POST /api/v1/data/imports/{id}/sheet` | read the upload again with another separator, encoding or header |
| `POST /api/v1/data/imports/{id}/preview` | check the first rows with `ImportSettings` |
| `POST /api/v1/data/imports/{id}/start` | run the import or a dry run (`dryRun: true`) as a job |
| `POST /api/v1/data/imports/{id}/undo` | undo the last import |
| `GET/POST /api/v1/data/{set}/import/profiles`, `DELETE /api/v1/data/import-profiles/{id}` | saved mappings |
| `GET /api/v1/data/jobs`, `GET /api/v1/data/jobs/{id}`, `GET /api/v1/data/jobs/{id}/file` | the user's jobs and their files |
| `GET /api/v1/data/{set}/export/stream`, `GET /api/v1/data/downloads/{token}` | export with progress lines, its one-time download (no sign-in) |
| `POST /api/v1/data/{set}/export/mail` | export as job, mailed |
| `POST /api/v1/data/{set}/import` | one-step import of earlier versions: columns named like the fields, new rows only |

**Status:** uploads and result files are kept in the job row in the database, which suits files of a few MB. Key and reference lookups run one query per row and new text. Undo deletes created rows only.
