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
| `POST /api/v1/data/{set}/import` | one-step import of earlier versions: columns named like the fields, new rows only |

**Status:** uploads and result files are kept in the job row in the database, which suits files of a few MB. Key and reference lookups run one query per row and new text. Undo deletes created rows only.
