# Extended attributes

`Coworkee.ExtendedAttributes` lets users attach free key/value pairs to any entity without a schema change. A value is text, a decimal, a date or JSON; attributes can be grouped, described, carry an external id and be switched off.

```csharp
[DependsOn(typeof(CoworkeeExtendedAttributesModule))]
public sealed class MyAppDocumentsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddExtendedAttributes<Document>("Documents", DocumentPermissions.Documents.View, DocumentPermissions.Documents.Edit);
}
```

The entity is checked through the `DbContext`, so tenant filters apply: attributes of another organisation's entity are not found. Saving replaces the whole list; a key may appear once, JSON values must be valid.

| Endpoint | |
|---|---|
| `GET /api/v1/attributes/{entityType}/{entityId}` | the attributes |
| `PUT /api/v1/attributes/{entityType}/{entityId}` | replace with `{ "attributes": [...] }` |

## In the client

```razor
<ExtendedAttributesEditor EntityType="Documents" EntityId="document.Id" />
```

or as a dialog from a table row:

```csharp
await ExtendedAttributesDialog.ShowAsync(Dialogs, L["Attributes"], "Documents", document.Id);
```
