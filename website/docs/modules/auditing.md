# Auditing

The audit trail records every change of the app's entities field by field: who (and who was signed in as him), when, old value, new value. It is written in the same transaction as the change (table `cw.AuditEntries`), so it never lies.

`Coworkee.Auditing` (`CoworkeeAuditingModule`) adds the audit log, its endpoints, the record history, the pages of the [security log](#security-log) and the [request log](#request-log); the template has it (option `auditing`, on by default). The trail itself is written by the app's DbContext.

## Audit trail

- Every added, changed and deleted entity is recorded, except owned types, [anonymous records](#anonymous-records) and what is excluded: `[NotAudited]` on a type or property (or `IsNotAudited()` in a model contributor) leaves it out, `[Sensitive]` (or `IsSensitive()`) records that a property changed without its value (`***`).
- The **Audit log** page (*Administration > Monitoring > Audit log*, `/cw/admin/audit`) lists the changes of the own organisation (the system organisation also sees entries without one) as a data table with search, the period, *Only my changes* and who signed in as the actor (*impersonated by*).
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` shows the history of one entity on its own page.
- Entities with `IVersioned` also keep full snapshots (`cw.EntitySnapshots`, a `Revision` per save). A module or app registers a type with an `IVersionedTypeContributor` (`context.Add<Theme>("Theme", permission)`); `<VersionHistory Type="Theme" Id="..." />` then lists the revisions and restores an older one for holders of that permission.

| Endpoint | |
|---|---|
| `GET /api/v1/audit?entityType=&entityId=&actorId=&impersonatorId=&from=&to=&page=&pageSize=` | the entries (`Audit.View`); also the OData set `AuditEntries` |
| `GET /api/v1/versions/{type}/{id}`, `GET .../{revision}`, `POST .../{revision}/restore` | snapshots of an `IVersioned` type (its registered permission) |

The permission `Audit.View` shows the audit log and the history. With the [Retention](retention.md) module the targets `audit` and `snapshots` delete old entries; the personal data export has a section `activity` (what the user changed: type, id, action, time), and erasing a user keeps the trail without him: his id becomes the erased-user placeholder, the values of the records erased with him become `***`, his id and address leave the other values ([rules](../security/privacy.md#the-audit-trail)).

## Record history and restore

The audit already knows every field change, so it can show a record's history and put it back. You opt in per entity set: the history then runs restores through your own update command, with its permissions, validation and audit.

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
context.Services.AddEntityHistory("Products", CatalogPermissions.Products.Edit, (Product p) => new AddEditProductCommand(p.Id, new AddEditProductRequest
{
    Name = p.Name, Barcode = p.Barcode, Description = p.Description, ImageDataUrl = p.ImageDataUrl, Rate = p.Rate, BrandId = p.BrandId,
}));
```

The second argument is the permission to restore. The lambda turns a record into the update command; a restore fills the record with the earlier values and sends it, so every field the command leaves out stays as it is.

In the client, either turn on the row action of a table or put the timeline on a detail page:

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" History="true" ... />

<CoworkeeEntityHistory EntitySet="Products" Id="product.Id" OnRestored="ReloadAsync" />
```

- The timeline shows who changed what and when, with field labels (`BrandId` reads *Brand*), enum names and the name of a referenced record. Long texts show a word diff.
- **Restore this state** puts the record back to how it was right after that entry; the undo button next to a field undoes just that change. Both show a preview of the resulting values and ask first.
- Records with `ISoftDelete` can come back too: the table's **Deleted records** button lists them, and restoring any state undeletes the record first.
- Seeing the history needs `Audit.View` and the read permission of the entity set; restoring needs the permission you registered. Everything stays in the user's tenant.
- `[Sensitive]` fields show as `***` and are never restored; properties hidden from the OData set stay out of the history.

The audit log (`/cw/admin/audit`) offers the same actions per entry when the entry's type belongs to such a set: open a row and pick **Undo this change** (only the fields of that entry go back, later changes of other fields stay), **Restore the state before this change** (that entry and everything after it is rolled back), **Restore record** for the delete of a soft-delete record, or **Open record history**. They use the same preview and confirmation. If a later entry changed one of the fields an undo puts back, the preview marks the field *Changed later* and warns that the newer value is replaced. Users without the restore permission see only the history; entries of other types show nothing.

The endpoints live under `/api/v1/history`: `GET /` (the sets the user may read, with `CanRestore`), and per set `/{entitySet}`: `GET /{id}`, `POST /{id}/preview`, `POST /{id}/restore` and `GET /deleted`. The request of preview and restore is `{ entryId, property?, mode, version? }` with `mode` `After` (default, the state right after the entry), `Before` or `Undo`. The preview returns a `version` of the record; the restore sends it back and answers `409` when the record changed in between (a value, the delete or a new history entry). The Blazor dialogs and the AirWorks record window then show the new preview.

Records that were hard deleted are gone from the table, so there is nothing to update; use `ISoftDelete` for anything people might want back.

## Security log

The security log records what happens to accounts: sign-ins (succeeded or failed, with the reason), sign-outs, lockouts, changes of passwords, e-mail addresses, two-step verification and passkeys, *Log in as* (start and end), API keys (created, revoked, the first use of a day) and the roles and own permissions a user gets or loses. It is always on and written by every host with the app database (auth server and API), in table `cw.SecurityEvents`.

Each entry has the time, the organisation, the **user** whose account it concerns, the **actor** when someone else did it (an administrator locking the user), the **impersonator** when someone was signed in as the acting user, the action and result with a reason (`InvalidCredentials`, `UnknownUser`, `LockedOut`, `NotAllowed`, `FailedAttempts`, `Administrator`, ...), the client, the IP address, the browser (user agent), the trace id and details as JSON (the login typed for an unknown account, the sign-in method, the role, the permission, the key).

- Changes of credentials and access are taken from the saves themselves (an EF Core interceptor of Coworkee.Identity), so every way to them counts: own profile, administration, reset links, failed attempts. They are saved in the same transaction as the change.
- Sign-ins, sign-outs and *Log in as* are written by the sign-in pages and the end session endpoint of the auth server; API keys by the key authentication.
- Your code writes entries of its own through `ISecurityLog` (Coworkee.Application.Auditing), for example when a session is ended:

```csharp
await securityLog.WriteAsync(new SecurityLogEntry(SecurityActions.SessionEnded, userId, Reason: "Signed out by an administrator", Details: new { sessionId }));
```

User, organisation and impersonator default to the current ones (the organisation is looked up from the user when the request has none); address, browser, client and trace come from the request. The entry is saved at once in a scope of its own, so it stays when the caller's unit of work does not. Keep secrets out of the details.

The page **Security log** (*Administration > Monitoring*, `/cw/admin/security-log`, permission `Audit.SecurityLog`) lists the entries of the own organisation (the system organisation also sees entries without one, such as failed sign-ins of unknown accounts) with search, period, facets for action, result and reason, and the details, browser and trace of a row. The user page links to the user's entries (`/cw/admin/security-log?user={id}`). The list is the OData set `SecurityEvents`.

## Request log

The request log records every API request of the API host: time, organisation, user (and who was signed in as him), client or API key, method, path and query string, route template, the request the endpoint dispatched (`IDispatcher`), status, duration, IP address, browser, trace id, a summary of an unhandled exception, the request and response headers and the request and response bodies. Table `cw.RequestLog`, written by `CoworkeeAuditingModule` in the host that has it (the API; the web app's backend for frontend forwards to it, so its calls are in the log once).

What it keeps and hides:

- **Headers**: all, with the values of `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` and every name containing *password*, *secret*, *token*, *apikey* or *credential* replaced by `***`. The same names are hidden in the query string and in bodies, at any depth (`clientSecret`, `refresh_token`, `nested.apiKey`); an object or list under such a name becomes `***` as a whole.
- **Bodies**: JSON (`application/json`, `*+json`, `text/json`) and forms (`application/x-www-form-urlencoded`, stored as a JSON object) only, each up to the size set (32 KB by default) and cut after it (marked by `…`; a cut body is redacted up to the cut). Files, multipart, NDJSON, server-sent events and binaries are never kept; streamed answers keep streaming.
- **Left out**: `OPTIONS` requests, health and telemetry (`/health`, `/alive`, `/metrics`), API docs (`/openapi`, `/swagger`), the realtime connection (`/hubs`), static files and endpoints marked with `.SkipRequestLog()` (or `[SkipRequestLog]`, Coworkee.AspNetCore). Unknown addresses under `/api` are logged (scanners, broken clients).
- **Address**: the connection's address. Behind the backend for frontend or a proxy that is the proxy unless the host applies forwarded headers (`UseForwardedHeaders`); `X-Forwarded-For` is in the headers either way.

Settings (*Settings > Request log*, Global; they apply at once on every instance):

| Setting | Default | |
|---|---|---|
| `RequestLog.Requests` (*Log requests*) | Reading, Writing | which requests are logged with headers and bodies: *Reading* (GET, HEAD, OData queries), *Writing* (POST, PUT, PATCH, DELETE); nothing chosen turns the request log off |
| `RequestLog.MaxBodyKb` (*Body size (KB)*) | 32 | kilobytes kept of each body; 0 keeps no bodies |
| `RequestLog.RedactedNames` (*Hidden names*) | | more header and field names to hide, comma separated |

**Performance.** The middleware only copies what passes by: the first kilobytes of each body while the endpoint reads or writes it, never the whole body. The request goes into a bounded queue (2000 requests) that a background service saves in batches of 200 with one EF Core save each; redaction happens there, off the request path. A full queue drops the request instead of slowing the app down and counts it in the metric `coworkee.request_log.dropped` of the meter `Coworkee.Auditing` (the template adds the meter to OpenTelemetry). Measured in the library's tests (in-process host, a 2 KB JSON body each way): about 0.1 ms more per request with both bodies kept.

The page **Request log** (*Administration > Monitoring*, `/cw/admin/request-log`, permission `Audit.Requests`) lists the requests of the own organisation (the system organisation also sees anonymous ones) with search, period and facets for method, route and status; the list never loads headers or bodies. Opening a row loads them; the bodies need `Audit.Requests.Bodies` (it implies `Audit.Requests`), since they may hold personal data. **Open trace** jumps to the trace in Grafana (Tempo) or the Aspire dashboard when the app host runs them (`Coworkee:Services:grafana` or `dashboard`).

| Endpoint | |
|---|---|
| OData `SecurityEvents` | the security log (`Audit.SecurityLog`) |
| OData `RequestLog` | the request log without headers and bodies (`Audit.Requests`) |
| `GET /api/v1/audit/requests/{id}` | one request with headers, and bodies for `Audit.Requests.Bodies` (`RequestLogDetailDto`, `BodiesHidden` otherwise) |
| `GET /api/v1/audit/trace-viewer` | `{ urlTemplate }` with `{traceId}`, or null without Grafana and dashboard (either log permission) |

The Blazor client has `IAuditApi` for the last two. With the [Retention](retention.md) module the targets `security-events` (365 days) and `request-log` (30 days) delete old entries. The personal data export has the sections `securityLog` and `requests`; erasing a user keeps the entries without him ([rules](../security/privacy.md)).

## Anonymous records

Some rows must not be traceable to the person who wrote them: answers of an anonymous survey, whistleblower reports, votes. Mark the type with `IAnonymous` (or derive from `AnonymousEntity`):

```csharp
public sealed class SurveyResponse : AnonymousEntity, IMultiTenant   // random Guid.NewGuid() id
{
    public Guid SurveyId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string? AnswersJson { get; set; }
    public Guid TenantId { get; set; }
}

response.SubmittedAt = response.Truncate(clock.GetUtcNow());   // own timestamps: cut them the same way
```

For such rows the library

- writes no `CreatedBy`, `ModifiedBy` or `DeletedBy`, and cuts `CreatedAt`, `ModifiedAt` and `DeletedAt` to the UTC day;
- writes **no audit trail entry** (so no record history either) and `IVersioned` snapshots without `CreatedBy` and with the cut time;
- sends domain events (outbox), webhooks and Elsa entity triggers **without user and trace id**, so their handlers run without a user; realtime change events never carry a user;
- does nothing to ids: `Entity` ids are time ordered (`Guid.CreateVersion7()`, millisecond precise), `AnonymousEntity` sets `Guid.NewGuid()`. A type with another base sets `Id = Guid.NewGuid()` in its constructor.

`TimeSpan IAnonymous.TimestampPrecision => TimeSpan.FromHours(1);` changes the precision (`TimeSpan.Zero` keeps exact times); a class deriving from `AnonymousEntity` lists `IAnonymous` again to do so.

What stays your job:

- Do not store a person on the row (`EmployeeId`) or let one reach it through a navigation, and mark child rows (`SurveyAnswer`) `IAnonymous` too.
- Small groups give people away: show results only from a minimum number of answers.
- Workflows, comments, reactions, files and notifications record their own actor; do not attach them to anonymous rows. Integration events you publish yourself (`IEventBus`) carry the current user: publish them inside `CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId))` when they belong to an anonymous row.
- Request logs and traces (OpenTelemetry) still show who called the endpoint at what time; keep their retention short or leave the endpoint out of detailed logging.
