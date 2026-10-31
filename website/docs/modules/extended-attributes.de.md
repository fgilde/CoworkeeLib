# Erweiterte Attribute

`Coworkee.ExtendedAttributes` lässt Benutzer freie Schlüssel/Wert-Paare an jede Entität hängen, ohne Schemaänderung. Ein Wert ist Text, eine Zahl, ein Datum oder JSON; Attribute lassen sich gruppieren, beschreiben, mit externer Id versehen und abschalten.

![Attribute eines Dokuments](../assets/screenshots/extended-attributes.png){ .shot }

## Einrichtung

```csharp
[DependsOn(typeof(CoworkeeExtendedAttributesModule))]
public sealed class MyAppDocumentsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddExtendedAttributes<Document>("Documents", DocumentPermissions.Documents.View, DocumentPermissions.Documents.Edit);
}
```

`AddExtendedAttributes<T>(entityType, viewPermission, editPermission)` registriert die Entität unter einem Namen (`"Documents"`) mit der Berechtigung zum Lesen und der zum Ändern ihrer Attribute. Attribute gehören über die `Guid Id` zu einer Zeile; eine anders geschlüsselte Entität wird bei der Registrierung abgelehnt. Die Attribute liegen in der Tabelle `cw.ExtendedAttributes` (eine Zeile je Schlüssel, eindeutig je Organisation, Entitätstyp, Entität und Schlüssel); lege eine Migration an, wenn du das Modul einschaltest.

## Im Client

```razor
<ExtendedAttributesEditor EntityType="Documents" EntityId="document.Id" />
```

oder als Dialog aus einer Tabellenzeile:

```csharp
await ExtendedAttributesDialog.ShowAsync(Dialogs, L["Attributes"], "Documents", document.Id);
```

Beide nehmen `ReadOnly`; der Editor meldet die gespeicherte Liste über `Saved`.

## Regeln

- Die Entität wird über ihren `DbContext` gesucht, Mandantenfilter und Soft Delete gelten also: Attribute einer Entität einer anderen Organisation werden nicht gefunden (`404`). Zeilenfilter der OData-Menge der Entität (`IODataEntityFilter<T>`) werden nicht geprüft; gib die Berechtigungen also nur Benutzern, die jede Zeile des Typs sehen dürfen.
- Lesen braucht die Lese-, Speichern die Bearbeitungsberechtigung (sonst `403`); ein unbekannter Entitätstyp antwortet mit `404`.
- Speichern ersetzt die ganze Liste: Schlüssel, die darin fehlen, werden gelöscht. Ein Schlüssel darf einmal vorkommen (ohne Rücksicht auf Groß- und Kleinschreibung), JSON-Werte müssen gültiges JSON sein; behalten wird nur der Wert des gewählten `Type` (`Text`, `Decimal`, `DateTime`, `Json`).

| Feld | Grenze |
|---|---|
| `Key` | Pflicht, 100 Zeichen |
| `Group` | 100 Zeichen |
| `Description` | 500 Zeichen |
| `ExternalId` | 200 Zeichen |

## Endpunkte

| Endpunkt | |
|---|---|
| `GET /api/v1/attributes/{entityType}/{entityId}` | die Attribute, nach Gruppe und Schlüssel sortiert |
| `PUT /api/v1/attributes/{entityType}/{entityId}` | ersetzen mit `{ "attributes": [...] }` |
