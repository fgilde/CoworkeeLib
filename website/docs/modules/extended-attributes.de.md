# Erweiterte Attribute

`Coworkee.ExtendedAttributes` lässt Benutzer freie Schlüssel/Wert-Paare an jede Entität hängen, ohne Schemaänderung. Ein Wert ist Text, eine Zahl, ein Datum oder JSON; Attribute lassen sich gruppieren, beschreiben, mit externer Id versehen und abschalten.

```csharp
[DependsOn(typeof(CoworkeeExtendedAttributesModule))]
public sealed class MyAppDocumentsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddExtendedAttributes<Document>("Documents", DocumentPermissions.Documents.View, DocumentPermissions.Documents.Edit);
}
```

Die Entität wird über den `DbContext` geprüft, Mandantenfilter gelten also: Attribute einer Entität einer anderen Organisation werden nicht gefunden. Speichern ersetzt die ganze Liste; ein Schlüssel darf einmal vorkommen, JSON-Werte müssen gültig sein.

| Endpunkt | |
|---|---|
| `GET /api/v1/attributes/{entityType}/{entityId}` | die Attribute |
| `PUT /api/v1/attributes/{entityType}/{entityId}` | ersetzt mit `{ "attributes": [...] }` |

## Im Client

```razor
<ExtendedAttributesEditor EntityType="Documents" EntityId="document.Id" />
```

oder als Dialog aus einer Tabellenzeile:

```csharp
await ExtendedAttributesDialog.ShowAsync(Dialogs, L["Attributes"], "Documents", document.Id);
```
