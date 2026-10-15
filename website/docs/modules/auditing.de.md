# Audit

Das Audit protokolliert jede Änderung an auditierten Entities Feld für Feld: wer, wann, alter Wert, neuer Wert. Es wird in derselben Transaktion wie die Änderung geschrieben und lügt deshalb nie.

- Die Seite **Audit log** listet alle Änderungen mit Filtern für Benutzer, Entity-Typ und Zeitraum.
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` zeigt die Historie einer Entity auf ihrer eigenen Seite.
- `[NotAudited]` nimmt einen Typ oder ein Feld aus, `[Sensitive]` protokolliert, dass sich ein Feld geändert hat, ohne den Wert.
- Entities mit `IVersioned` behalten zusätzlich vollständige Schnappschüsse; `<VersionHistory Type="..." Id="..." />` listet sie und stellt eine ältere Revision wieder her.

## Verlauf eines Datensatzes und Wiederherstellen

Das Audit kennt schon jede Feldänderung, also kann es auch den Verlauf eines Datensatzes zeigen und ihn zurückholen. Du schaltest das pro Entity-Set ein. Wiederherstellen läuft dann über deinen eigenen Update-Command, mit dessen Berechtigungen, Validierung und Audit.

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
context.Services.AddEntityHistory("Products", CatalogPermissions.Products.Edit, (Product p) => new AddEditProductCommand(p.Id, new AddEditProductRequest
{
    Name = p.Name, Barcode = p.Barcode, Description = p.Description, ImageDataUrl = p.ImageDataUrl, Rate = p.Rate, BrandId = p.BrandId,
}));
```

Das zweite Argument ist die Berechtigung zum Wiederherstellen. Das Lambda macht aus einem Datensatz den Update-Command. Beim Wiederherstellen bekommt der Datensatz die früheren Werte und geht so in den Command; Felder, die der Command nicht kennt, bleiben, wie sie sind.

Im Client schaltest du entweder die Zeilenaktion einer Tabelle ein oder setzt die Zeitleiste auf eine Detailseite:

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" History="true" ... />

<CoworkeeEntityHistory EntitySet="Products" Id="product.Id" OnRestored="ReloadAsync" />
```

- Die Zeitleiste zeigt, wer wann was geändert hat, mit Feldnamen (`BrandId` wird zu *Brand*), Enum-Namen und dem Namen eines verknüpften Datensatzes. Lange Texte zeigen einen Wort-Diff.
- **Diesen Stand wiederherstellen** setzt den Datensatz auf den Stand direkt nach diesem Eintrag zurück; der Rückgängig-Knopf neben einem Feld macht nur diese eine Änderung rückgängig. Beides zeigt vorher, welche Werte herauskommen, und fragt nach.
- Datensätze mit `ISoftDelete` kommen auch zurück: Der Knopf **Gelöschte Datensätze** der Tabelle listet sie, und beim Wiederherstellen wird der Datensatz zuerst wieder aktiv.
- Für den Verlauf brauchst du `Audit.View` und die Leseberechtigung des Entity-Sets, fürs Wiederherstellen die registrierte Berechtigung. Alles bleibt im Mandanten des Benutzers.
- `[Sensitive]`-Felder erscheinen als `***` und werden nie zurückgeschrieben; Felder, die das OData-Set verbirgt, tauchen im Verlauf nicht auf.

Die Endpunkte liegen unter `/api/v1/history/{entitySet}`: `GET /{id}`, `POST /{id}/preview`, `POST /{id}/restore` und `GET /deleted`.

Hart gelöschte Datensätze gibt es in der Tabelle nicht mehr, also gibt es auch nichts zu aktualisieren. Nimm `ISoftDelete` für alles, was jemand vielleicht zurückhaben möchte.
