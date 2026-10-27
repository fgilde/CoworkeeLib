# Import und Export

Deine Nutzer holen Zeilen aus CSV-, Excel- oder JSON-Dateien herein und bringen sie wieder hinaus, ohne dass du einen Parser oder eine Upload-Seite schreibst. Jede importierte Zeile läuft durch deinen eigenen Command. Berechtigungen, Organisation, Validierung und Audit-Log gelten also genauso, als hätte jemand das Formular ausgefüllt.

## Freischalten pro Entity-Set

Ein Entity-Set wird importierbar, sobald du registrierst, wie aus einer Zeile ein Command wird. Sets ohne Registrierung lassen sich nur exportieren.

```csharp
// Anlegen und Ändern: die Id ist bei neuen Zeilen null, bei Änderungen die der gefundenen Zeile
services.AddODataImport("Brands",
    (Guid? id, AddEditBrandRequest row) => new AddEditBrandCommand(id, row),
    id => new DeleteBrandsCommand([id]));   // optional: der letzte Import lässt sich rückgängig machen

// nur Anlegen
services.AddODataImport("Notes", (AddNoteRequest row) => new AddNoteCommand(row));
services.AddODataImport<AddNoteCommand>("Notes");   // der Command selbst ist die Zeile
```

Die öffentlichen Eigenschaften des Zeilentyps mit einfachen Typen (Text, Zahlen, Datum, `Guid`, `bool`, Enums) sind die Felder, denen du Spalten zuordnen kannst. Die Beschriftung kommt aus `[Display]`, `[DisplayName]` oder `[Description]`, sonst aus dem Namen ("MadeAt" wird zu "Made at"). `[Required]` und `required` markieren Felder, nach denen der Assistent fragt.

Eine `Guid`-Eigenschaft wie `BrandId` neben einer Navigation `Brand` auf ein anderes OData-Set ist ein Verweis: In der Datei darf statt der Id der Name der Marke stehen. Der Name ist die erste vorhandene Eigenschaft aus `Name`, `Title`, `DisplayName`, `Code` und `Key`, sonst die erste Text-Eigenschaft.

## Der Assistent

`CoworkeeDataTable` zeigt mit `Importable="true"` einen *Import*-Button. Er öffnet ein Seitenpanel mit vier Schritten:

1. **Datei**: CSV, TSV, Excel (`.xlsx`) oder JSON bis 20 MB. Bei Textdateien erkennt der Assistent Kodierung (Byte Order Mark, sonst UTF-8 oder Windows-1252), Trennzeichen und ob die erste Zeile Spaltennamen enthält. Alles davon kannst du korrigieren.
2. **Spalten**: Jede Spalte bekommt ein vorgeschlagenes Feld, gefunden über Namen, Beschriftung oder die Beschriftung in der Sprache des Nutzers. Du wählst *neue Zeilen anlegen*, *vorhandene Zeilen ändern* oder *ändern oder anlegen*. Die beiden letzten brauchen eine Schlüsselspalte, etwa die Id oder den Namen. Bei Textdateien legst du die Sprache für Zahlen und Datumswerte fest (`1.234,50` und `15.01.2026` in `de-DE`). Eine Zuordnung kannst du pro Entity-Set unter einem Namen speichern und für die nächste Datei desselben Lieferanten wieder laden.
3. **Prüfen**: Die ersten 200 Zeilen laufen durch Umwandlung, Schlüsselsuche und die FluentValidation-Validatoren der Commands, ohne dass etwas geschrieben wird. Du siehst, was jede Zeile tun würde und warum eine Zeile scheitert.
4. **Import**: Ein *Probelauf* prüft im Hintergrund jede Zeile, danach schreibt *Import* sie. Beides läuft als Hintergrundjob, der Fortschritt kommt live über das Realtime-Topic des Nutzers. Fehlerhafte Zeilen lädst du als CSV mit einer zusätzlichen Spalte *Error* herunter, korrigierst sie und importierst sie erneut. *Import rückgängig machen* löscht die Zeilen, die der letzte Import des Nutzers in diesem Set angelegt hat (geänderte Zeilen behalten ihre neuen Werte).

Änderungen starten bei der gespeicherten Zeile: Spalten, die du nicht zuordnest, behalten ihren Wert.

So werden Werte gelesen:

| Typ | Akzeptiert |
|---|---|
| Zahlen | bei Textdateien in der gewählten Sprache, bei Excel und JSON sprachneutral; Ganzzahlen lehnen Nachkommastellen ab |
| Datum | zuerst ISO (`2026-01-15`), dann die gewählte Sprache |
| Wahrheitswerte | `true/false`, `yes/no`, `y/n`, `1/0`, `x`, `on/off` sowie *Ja*/*Nein* in der Sprache des Nutzers |
| Enums | Name, Zahl, Beschriftung (`[Display]`/`[Description]`) oder deren Übersetzung |
| Verweise | Id oder Anzeigetext, nur Zeilen, die der Nutzer lesen darf |

## Admin-Seite

*Administration → System → Import und Export* (`/admin/data-transfer`, Berechtigung `DataTransfer.Use`) listet alle Sets, die der Nutzer importieren darf, exportiert ganze Sets und zeigt seine Importe und Exporte mit Ergebnisdateien und Rückgängig-Funktion.

## Export

Das Export-Menü einer Tabelle schreibt die aktuelle Ansicht: Suche, Facetten, Spaltenfilter, Sortierung und die sichtbaren Spalten. Excel, CSV und JSON kommen vom Server:

```http
GET /api/v1/data/Products/export?format=csv&columns=Name,Rate&$filter=Rate gt 10&$orderby=Name
```

Bis `Coworkee:OData:BackgroundExportRows` Zeilen (Standard 10 000) kommt die Datei sofort zurück. Größere Exporte antworten mit `202 Accepted` und einem Job; der Nutzer bekommt einen Hinweis mit *Herunterladen*-Button, sobald die Datei fertig ist. `Coworkee:OData:MaxExportRows` (Standard 100 000) begrenzt jeden Export. Berechtigungen, Entity-Filter und ausgeblendete Eigenschaften gelten wie in der Liste.

### Fortschritts-Stream und einmaliger Download

`GET /api/v1/data/{set}/export/stream` nimmt dieselbe Abfrage und antwortet mit `application/x-ndjson`, ein `DataExportProgress` pro Zeile: `counting`, `reading` (0-80 %, alle 500 Zeilen), `writing`, dann `ready` mit `url` (`api/v1/data/downloads/{token}`, relativ zur API), `fileName` und `jobId` oder `failed` mit einer Meldung. Der Link braucht keine Anmeldung, ein nativer `<a download>` funktioniert also; er öffnet die Datei einmal und nur fünf Minuten lang (`IAccessTokens`, siehe [Dateien](../modules/files.md#temporare-links)). Der Export läuft ohne den Abbruch der Anfrage: Verlässt der Client die Seite vor `ready`, wird er trotzdem geschrieben und dem Nutzer gemailt. So oder so behält die Job-Liste des Nutzers die Datei. `IDataTransferApi.StreamExportAsync` liest die Zeilen im Blazor-Client.

Mehr Zeilen als `BackgroundExportRows` beenden den Stream mit `queued` und der `jobId`: Der Export-Job schreibt die Datei wie bei jedem Hintergrund-Export (und mailt sie, wenn der Client schon gegangen war). Ein Benutzer hat höchstens `Coworkee:OData:MaxConcurrentExports` (Standard 2) laufende oder wartende Exporte; ein weiterer Stream, Hintergrund-Export (`GET .../export` über `BackgroundExportRows`) oder gemailter Export antwortet mit `429`. Prüfung und neuer Job laufen unter einer Sperre pro Benutzer, parallele Anfragen kommen also nicht gemeinsam durch. Ein gestreamter Export wird nach `Coworkee:OData:ExportTimeout` (Standard 30 Minuten) abgebrochen und sein Job schlägt fehl; laufende oder wartende Exporte, die älter sind (ein Server wurde beim Schreiben beendet), schlagen fehl: Der Job `coworkee-stale-exports` räumt sie alle fünf Minuten auf, die eigenen eines Benutzers auch, wenn er den nächsten Stream startet, so dass keiner für immer läuft.

### Gemailte und geplante Exporte

`POST /api/v1/data/{set}/export/mail` mit `MailExportRequest(Query, Recipients, FileName)` schreibt den Export als Hintergrund-Job und mailt ihn dem Nutzer und den Empfängern (`202` mit dem Job). Andere Empfänger als der Nutzer brauchen `DataTransfer.ExportSchedules`.

Geplante Exporte (`DataTransfer.ExportSchedules` plus die eigene Berechtigung des Sets) mailen eine Liste nach Plan: `ExportSchedule` (Tabelle `cw.ExportSchedules`) speichert das Set, die Abfrage unverändert (`$filter`, `$search`, `$orderby`, `columns`, `format`), einen Namen, einen Dateinamen, weitere Empfänger, die ausgewählten Gruppen und einen `ReportSchedule`: täglich, wöchentlich an einem Wochentag oder monatlich an einem Tag oder am ersten bis vierten oder letzten Wochentag (`Week`), jede n-te Periode (`Every`), zu einer Uhrzeit einer Zeitzone. Der Job `coworkee-export-schedules` (alle fünf Minuten) führt jeden fälligen Plan als sein Besitzer aus, so dass dessen Berechtigungen, Zeilenfilter und Gruppen bestimmen, was die Datei enthält; jeder Lauf ist ein Job des Besitzers mit der Datei, Fehler bleiben am Plan (`LastError`). Ein Lauf sendet nichts mehr, sobald der Besitzer deaktiviert ist oder `DataTransfer.ExportSchedules` oder die Berechtigung des Sets verloren hat; der Grund steht in `LastError` des Plans. Ein Plan oder Hintergrund-Export, der mit ausgewählten Gruppen gespeichert wurde, listet nur die, auf die der Besitzer noch Zugriff hat; ist keine mehr übrig, schlägt er fehl, statt alle Gruppen zu listen.

| Aufruf | Zweck |
|---|---|
| `GET /api/v1/data/export-schedules?entitySet=` | die Pläne des Nutzers |
| `POST /api/v1/data/export-schedules`, `PUT .../{id}` | einen anlegen oder ändern (`SaveExportScheduleRequest`) |
| `DELETE /api/v1/data/export-schedules/{id}` | einen löschen |
| `POST /api/v1/data/export-schedules/{id}/run` | jetzt senden; der Plan bleibt |

Das Export-Menü einer Tabelle bietet Inhabern der Berechtigung *Export planen*; die Seite *Geplante Exporte* (`/data/export-schedules`, Schlüssel `export-schedules`) listet sie mit *Jetzt senden*, Bearbeiten und Löschen. Die Mail ist die Vorlage `Data.Export` (Mailing-Modul, 23 Sprachen); Dateien über 10 MB werden nicht angehängt, die Mail verweist dann auf die App. Ohne Mailing-Modul wird nichts gemailt (`IExportMailer`). App-Werte wie eine Währung bleiben in der Abfrage; die App liest sie selbst.

## HTTP-API

| Aufruf | Zweck |
|---|---|
| `GET /api/v1/data/imports` | importierbare Sets mit ihren Feldern |
| `POST /api/v1/data/{set}/import/file` | Upload (multipart, Feld `file`), antwortet mit den erkannten Spalten |
| `POST /api/v1/data/imports/{id}/sheet` | Upload mit anderem Trennzeichen, anderer Kodierung oder Kopfzeile neu lesen |
| `POST /api/v1/data/imports/{id}/preview` | die ersten Zeilen mit `ImportSettings` prüfen |
| `POST /api/v1/data/imports/{id}/start` | Import oder Probelauf (`dryRun: true`) als Job starten |
| `POST /api/v1/data/imports/{id}/undo` | den letzten Import rückgängig machen |
| `GET/POST /api/v1/data/{set}/import/profiles`, `DELETE /api/v1/data/import-profiles/{id}` | gespeicherte Zuordnungen |
| `GET /api/v1/data/jobs`, `GET /api/v1/data/jobs/{id}`, `GET /api/v1/data/jobs/{id}/file` | die Jobs des Nutzers und ihre Dateien |
| `GET /api/v1/data/{set}/export/stream`, `GET /api/v1/data/downloads/{token}` | Export mit Fortschrittszeilen, sein einmaliger Download (ohne Anmeldung) |
| `POST /api/v1/data/{set}/export/mail` | Export als Job, gemailt |
| `POST /api/v1/data/{set}/import` | Import früherer Versionen in einem Schritt: Spalten heißen wie die Felder, nur neue Zeilen |

**Stand:** Uploads und Ergebnisdateien liegen in der Job-Zeile in der Datenbank, das passt für Dateien von wenigen MB. Schlüssel- und Verweissuche stellen eine Abfrage pro Zeile und neuem Text. Rückgängig löscht nur angelegte Zeilen.
