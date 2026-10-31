# Auditing

The audit trail records every change of the app's entities field by field: who (and who was signed in as him), when, old value, new value. It is written in the same transaction as the change (table `cw.AuditEntries`), so it never lies.

`Coworkee.Auditing` (`CoworkeeAuditingModule`) adds the audit log, its endpoints and the record history; the template has it (option `auditing`, on by default). The trail itself is written by the app's DbContext.

## Audit trail

- Every added, changed and deleted entity is recorded, except owned types, [anonymous records](#anonymous-records) and what is excluded: `[NotAudited]` on a type or property (or `IsNotAudited()` in a model contributor) leaves it out, `[Sensitive]` (or `IsSensitive()`) records that a property changed without its value (`***`).
- The **Audit log** page (*Administration > Monitoring > Audit log*, `/cw/admin/audit`) lists the changes of the own organisation (the system organisation also sees entries without one) as a data table with search, the period, *Only my changes* and who signed in as the actor (*impersonated by*).
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` shows the history of one entity on its own page.
- Entities with `IVersioned` also keep full snapshots (`cw.EntitySnapshots`, a `Revision` per save). A module or app registers a type with an `IVersionedTypeContributor` (`context.Add<Theme>("Theme", permission)`); `<VersionHistory Type="Theme" Id="..." />` then lists the revisions and restores an older one for holders of that permission.

| Endpoint | |
|---|---|
| `GET /api/v1/audit?entityType=&entityId=&actorId=&impersonatorId=&from=&to=&page=&pageSize=` | the entries (`Audit.View`); also the OData set `AuditEntries` |
| `GET /api/v1/versions/{type}/{id}`, `GET .../{revision}`, `POST .../{revision}/restore` | snapshots of an `IVersioned` type (its registered permission) |

The permission `Audit.View` shows the audit log and the history. With the [Retention](retention.md) module the targets `audit` and `snapshots` delete old entries; the personal data export has a section `activity` (what the user changed: type, id, action, time), and erasing a user keeps the trail without his name.

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
