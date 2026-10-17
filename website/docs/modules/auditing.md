# Auditing

The audit trail records every change of audited entities field by field: who, when, old value, new value. It is written in the same transaction as the change, so it never lies.

- The **Audit log** page lists all changes with filters for user, entity type and time.
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` shows the history of one entity on its own page.
- `[NotAudited]` excludes a type or property, `[Sensitive]` records that a property changed without its value.
- Entities with `IVersioned` also keep full snapshots; `<VersionHistory Type="..." Id="..." />` lists them and restores an older revision.

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

The audit log (`/admin/audit`) offers the same actions per entry when the entry's type belongs to such a set: open a row and pick **Undo this change** (only the fields of that entry go back, later changes of other fields stay), **Restore the state before this change** (that entry and everything after it is rolled back), **Restore record** for the delete of a soft-delete record, or **Open record history**. They use the same preview and confirmation. If a later entry changed one of the fields an undo puts back, the preview marks the field *Changed later* and warns that the newer value is replaced. Users without the restore permission see only the history; entries of other types show nothing.

The endpoints live under `/api/v1/history`: `GET /` (the sets the user may read, with `CanRestore`), and per set `/{entitySet}`: `GET /{id}`, `POST /{id}/preview`, `POST /{id}/restore` and `GET /deleted`. The request of preview and restore is `{ entryId, property?, mode, version? }` with `mode` `After` (default, the state right after the entry), `Before` or `Undo`. The preview returns a `version` of the record; the restore sends it back and answers `409` when the record changed in between (a value, the delete or a new history entry). The Blazor dialogs and the AirWorks record window then show the new preview.

Records that were hard deleted are gone from the table, so there is nothing to update; use `ISoftDelete` for anything people might want back.
