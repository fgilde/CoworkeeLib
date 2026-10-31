# Audit

Das Audit protokolliert jede Änderung an den Entities der App Feld für Feld: wer (und wer als er angemeldet war), wann, alter Wert, neuer Wert. Es wird in derselben Transaktion wie die Änderung geschrieben (Tabelle `cw.AuditEntries`) und lügt deshalb nie.

`Coworkee.Auditing` (`CoworkeeAuditingModule`) bringt das Audit-Log, seine Endpunkte und den Verlauf eines Datensatzes; die Vorlage hat es (Option `auditing`, standardmäßig an). Das Protokoll selbst schreibt der DbContext der App.

## Audit-Protokoll

- Jede angelegte, geänderte und gelöschte Entity wird protokolliert, außer Owned Types, [anonyme Datensätze](#anonyme-datensatze) und was ausgenommen ist: `[NotAudited]` an einem Typ oder Feld (oder `IsNotAudited()` in einem Model-Contributor) lässt es weg, `[Sensitive]` (oder `IsSensitive()`) protokolliert, dass sich ein Feld geändert hat, ohne den Wert (`***`).
- Die Seite **Audit-Log** (*Administration > Überwachung > Audit-Log*, `/cw/admin/audit`) listet die Änderungen der eigenen Organisation (die System-Organisation sieht auch Einträge ohne Organisation) als Datentabelle mit Suche, Zeitraum, *Nur meine Änderungen* und wer als der Akteur angemeldet war (*angemeldet durch*).
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` zeigt die Historie einer Entity auf ihrer eigenen Seite.
- Entities mit `IVersioned` behalten zusätzlich vollständige Schnappschüsse (`cw.EntitySnapshots`, eine `Revision` pro Speichern). Ein Modul oder eine App registriert einen Typ mit einem `IVersionedTypeContributor` (`context.Add<Theme>("Theme", permission)`); `<VersionHistory Type="Theme" Id="..." />` listet dann die Revisionen und stellt für Inhaber dieser Berechtigung eine ältere wieder her.

| Endpunkt | |
|---|---|
| `GET /api/v1/audit?entityType=&entityId=&actorId=&impersonatorId=&from=&to=&page=&pageSize=` | die Einträge (`Audit.View`); auch das OData-Set `AuditEntries` |
| `GET /api/v1/versions/{type}/{id}`, `GET .../{revision}`, `POST .../{revision}/restore` | Schnappschüsse eines `IVersioned`-Typs (mit seiner registrierten Berechtigung) |

Die Berechtigung `Audit.View` zeigt das Audit-Log und den Verlauf. Mit dem Modul [Aufbewahrung](retention.md) löschen die Ziele `audit` und `snapshots` alte Einträge; der Export personenbezogener Daten hat einen Abschnitt `activity` (was der Benutzer geändert hat: Typ, Id, Aktion, Zeit), und das Löschen eines Benutzers behält das Protokoll ohne seinen Namen.

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

Das Audit-Log (`/cw/admin/audit`) bietet dieselben Aktionen pro Eintrag, wenn dessen Typ zu so einem Set gehört: Öffne eine Zeile und wähle **Diese Änderung rückgängig machen** (nur die Felder dieses Eintrags gehen zurück, spätere Änderungen an anderen Feldern bleiben), **Stand vor dieser Änderung wiederherstellen** (dieser Eintrag und alles danach wird zurückgerollt), **Datensatz wiederherstellen** beim Löschen eines Soft-Delete-Datensatzes oder **Verlauf des Datensatzes öffnen**. Alle zeigen dieselbe Vorschau und fragen nach. Hat ein späterer Eintrag ein Feld geändert, das ein Rückgängigmachen zurücksetzt, markiert die Vorschau das Feld mit *Später geändert* und warnt dich, dass der neuere Wert ersetzt wird. Ohne die Berechtigung zum Wiederherstellen siehst du nur den Verlauf; Einträge anderer Typen zeigen nichts.

Die Endpunkte liegen unter `/api/v1/history`: `GET /` (die Sets, die du lesen darfst, mit `CanRestore`) und pro Set `/{entitySet}`: `GET /{id}`, `POST /{id}/preview`, `POST /{id}/restore` und `GET /deleted`. Vorschau und Wiederherstellen nehmen `{ entryId, property?, mode, version? }` mit `mode` `After` (Standard, der Stand direkt nach dem Eintrag), `Before` oder `Undo`. Die Vorschau liefert eine `version` des Datensatzes; das Wiederherstellen schickt sie zurück und antwortet `409`, wenn sich der Datensatz dazwischen geändert hat (ein Wert, das Löschen oder ein neuer Verlaufseintrag). Die Blazor-Dialoge und das AirWorks-Datensatzfenster zeigen dann die neue Vorschau.

Hart gelöschte Datensätze gibt es in der Tabelle nicht mehr, also gibt es auch nichts zu aktualisieren. Nimm `ISoftDelete` für alles, was jemand vielleicht zurückhaben möchte.

## Anonyme Datensätze

Manche Zeilen dürfen nicht auf die Person zurückführbar sein, die sie geschrieben hat: Antworten einer anonymen Umfrage, Hinweisgebermeldungen, Abstimmungen. Markiere den Typ mit `IAnonymous` (oder leite von `AnonymousEntity` ab):

```csharp
public sealed class SurveyResponse : AnonymousEntity, IMultiTenant   // zufällige ID mit Guid.NewGuid()
{
    public Guid SurveyId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string? AnswersJson { get; set; }
    public Guid TenantId { get; set; }
}

response.SubmittedAt = response.Truncate(clock.GetUtcNow());   // eigene Zeitstempel genauso kürzen
```

Für solche Zeilen

- schreibt die Bibliothek kein `CreatedBy`, `ModifiedBy` oder `DeletedBy` und kürzt `CreatedAt`, `ModifiedAt` und `DeletedAt` auf den UTC-Tag;
- schreibt sie **keinen Audit-Eintrag** (also auch keinen Verlauf) und `IVersioned`-Snapshots ohne `CreatedBy` und mit gekürzter Zeit;
- verschickt sie Domain Events (Outbox), Webhooks und Elsa-Entity-Trigger **ohne Benutzer und Trace-ID**, ihre Handler laufen also ohne Benutzer; Realtime-Änderungsereignisse tragen nie einen Benutzer;
- lässt sie IDs, wie sie sind: IDs von `Entity` sind zeitlich sortiert (`Guid.CreateVersion7()`, auf die Millisekunde), `AnonymousEntity` setzt `Guid.NewGuid()`. Ein Typ mit anderer Basisklasse setzt `Id = Guid.NewGuid()` in seinem Konstruktor.

`TimeSpan IAnonymous.TimestampPrecision => TimeSpan.FromHours(1);` ändert die Genauigkeit (`TimeSpan.Zero` behält exakte Zeiten); eine Klasse, die von `AnonymousEntity` erbt, führt dafür `IAnonymous` noch einmal auf.

Was deine Aufgabe bleibt:

- Keine Person an der Zeile speichern (`EmployeeId`) oder über eine Navigation erreichbar machen, und Kind-Zeilen (`SurveyAnswer`) ebenfalls mit `IAnonymous` markieren.
- Kleine Gruppen verraten Personen: Ergebnisse erst ab einer Mindestzahl an Antworten zeigen.
- Workflows, Kommentare, Reaktionen, Dateien und Benachrichtigungen speichern ihren eigenen Akteur; hänge sie nicht an anonyme Zeilen. Integration Events, die du selbst veröffentlichst (`IEventBus`), tragen den aktuellen Benutzer: veröffentliche sie in `CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId))`, wenn sie zu einer anonymen Zeile gehören.
- Request-Logs und Traces (OpenTelemetry) zeigen weiterhin, wer den Endpunkt wann aufgerufen hat; halte ihre Aufbewahrung kurz oder nimm den Endpunkt aus dem detaillierten Logging.
