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

1. **Datei**: CSV, TSV, TXT, Excel (`.xlsx`) oder JSON bis 20 MB. Bei Textdateien erkennt der Assistent Kodierung (Byte Order Mark, sonst UTF-8 oder Windows-1252), Trennzeichen und ob die erste Zeile Spaltennamen enthält. Alles davon kannst du korrigieren.
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

Die Abfrage nimmt `$filter`, `$search` und `$orderby` der Liste und vier eigene Parameter:

| Parameter | |
|---|---|
| `format` | `xlsx` (der Standard, auch bei einem unbekannten Wert), `csv` oder `json`. Excel hat eine fette, fixierte Kopfzeile mit Filtern; CSV schreibt Datumswerte als ISO 8601 und Zahlen kulturunabhängig; JSON schreibt Enums mit ihrem Namen. |
| `columns` | die Felder in ihrer Reihenfolge, durch Kommas getrennt, jedes mit optionaler Überschrift nach einem Doppelpunkt: `columns=Name:Produkt,Rate:Satz (%),Category`. CSV und Excel überschreiben die Spalte damit, JSON behält die Feldnamen als Schlüssel; ohne Überschrift steht der Feldname darüber. Ein Backslash maskiert ein Komma oder einen Doppelpunkt in einer Überschrift (`Weight:Gewicht\, kg`); `ExportColumns.Format` und `ExportColumns.Parse` (`Coworkee.Contracts.Data`) schreiben und lesen den Wert. Groß- und Kleinschreibung der Felder zählt nicht; unbekannte Felder und Felder, die der Benutzer nicht lesen darf, fallen weg. Ohne `columns` enthält der Export alle Spalten des Sets: seine eigenen Eigenschaften einfacher Typen und die [berechneten Eigenschaften](odata.md#berechnete-eigenschaften) mit `Exported`. |
| `route` | die OData-Route, deren Namen die Abfrage verwendet, z. B. `route=rt/odata` für eine mit `AddCoworkeeODataRoute` hinzugefügte Route (camelCase, Aliase wie `creationTime`): `$filter`, `$orderby` und `columns` werden in ihren Namen gelesen, und die Spalten heißen, wie die Route sie nennt. Ohne sie gelten die Namen von `/odata`; eine unbekannte Route antwortet mit `400`. Berechtigung, ausgeblendete und geschützte Eigenschaften, Zeilenfilter und Gruppen des Sets gelten wie auf der Route. |
| `fileName` | der Name der Datei ohne oder mit der Endung des Formats (`fileName=Produkte der Woche`); Zeichen, die ein Dateiname nicht enthalten kann, fallen weg, höchstens 200 bleiben. Ohne ihn der Name des Sets. |

Gemailte und geplante Exporte behalten sie alle mit ihrer Abfrage; der Stream nimmt sie ebenso.

Bis `Coworkee:OData:BackgroundExportRows` Zeilen (Standard 10 000) kommt die Datei sofort zurück. Größere Exporte antworten mit `202 Accepted` und einem Job; der Nutzer bekommt einen Hinweis mit *Herunterladen*-Button, sobald die Datei fertig ist. `Coworkee:OData:MaxExportRows` (Standard 100 000) begrenzt jeden Export: weitere Zeilen werden nicht geschrieben. Berechtigungen, Entity-Filter und ausgeblendete Eigenschaften gelten wie in der Liste.

### Fortschritts-Stream und einmaliger Download

`GET /api/v1/data/{set}/export/stream` nimmt dieselbe Abfrage und antwortet mit `application/x-ndjson`, ein `DataExportProgress` pro Zeile: `counting`, `reading` (0-80 %, alle 500 Zeilen), `writing`, dann `ready` mit `url` (`api/v1/data/downloads/{token}`, relativ zur API), `fileName` und `jobId`, oder `failed` mit einer Meldung, oder `cancelled`. Jede Zeile trägt die `jobId`, und die erste (`counting`) geht sofort hinaus, so dass ein Client abbrechen kann, bevor sonst etwas ankommt; Zeilen `reading` und `writing` tragen außerdem `rows` (bisher gelesen) und `total` (Zeilen des Exports).

Der Link braucht keine Anmeldung, ein nativer `<a download>` funktioniert also; er öffnet die Datei einmal und nur fünf Minuten lang (`IAccessTokens`, siehe [Dateien](../modules/files.md#temporare-links)). Der Export läuft ohne den Abbruch der Anfrage: Verlässt der Client die Seite vor `ready`, wird er trotzdem geschrieben und dem Nutzer gemailt. So oder so behält die Job-Liste des Nutzers die Datei. `IDataTransferApi.StreamExportAsync` liest die Zeilen im Blazor-Client.

Mehr Zeilen als `BackgroundExportRows` gehen an den Export-Job, und der Stream folgt ihm mit derselben `jobId`: Der Job speichert seinen Fortschritt jede Sekunde (`Total` und `Processed` des Jobs), den der Stream als `reading`- und `writing`-Zeilen sendet, und ist die Datei geschrieben, endet der Stream mit `ready` und dem einmaligen Link oder mit `failed` oder `cancelled`. Geht der Client inzwischen, mailt der Job die Datei dem Besitzer des Exports, sobald sie geschrieben ist (`DataTransferJob.MailOwner`), oder der Stream tut es, wenn der Job schon fertig war; sonst bekommt sie niemand. Server vor 0.0.1.50 beendeten einen solchen Stream mit `queued`.

Das Export-Menü von `CoworkeeDataTable` nutzt den Stream für Excel, CSV und JSON: Ein Dialog zeigt die Phase, die Zeilen (*500 von 2000 Zeilen*) und die Prozent und speichert die Datei über den einmaligen Link, sobald sie fertig ist (`FileDownloader.DownloadUrlAsync`, benannt nach `ExportFileName` plus Endung). *Abbrechen* bricht den Export auf dem Server ab; vor der ersten Zeile gedrückt, wartet es auf die Job-Id, die diese Zeile trägt. `429` (zu viele Exporte), `400` (ungültige Abfrage), eine `failed`-Zeile (mit ihrer Meldung, etwa *Der Export hat zu lange gedauert.*) und eine abgerissene Verbindung erscheinen als Fehler. `ExportProgressDialog.ShowAsync(dialogs, title, set, query, fileName)` startet denselben Dialog für eigene Buttons; `ExportQueries.Of` baut die Abfrage.

### Einen Export abbrechen

`POST /api/v1/data/jobs/{id}/cancel` (`IDataTransferApi.CancelJobAsync`) bricht einen laufenden oder wartenden Export des Benutzers ab: Der Job wird `Cancelled`, das gibt seinen Platz in `MaxConcurrentExports` sofort frei, und der Export endet ohne Datei und ohne Mail; der Stream endet mit einer Zeile `cancelled`. Die Job-Zeile ist maßgeblich: Die Instanz, die die Anfrage annimmt, stoppt den Export sofort, eine andere Instanz, auf der er läuft, merkt es binnen zwei Sekunden, und ein wartender Export startet gar nicht. Ein fertiger Job kommt unverändert zurück; der Job eines anderen Benutzers antwortet `404`, wie `GET /api/v1/data/jobs/{id}`.

### Grenzen und liegengebliebene Exporte

Ein Benutzer hat höchstens `Coworkee:OData:MaxConcurrentExports` (Standard 2) laufende oder wartende Exporte; ein weiterer Stream, Hintergrund-Export (`GET .../export` über `BackgroundExportRows`) oder gemailter Export antwortet mit `429`. Prüfung und neuer Job laufen unter einer Sperre pro Benutzer, parallele Anfragen kommen also nicht gemeinsam durch. Ein gestreamter Export wird nach `Coworkee:OData:ExportTimeout` (Standard 30 Minuten) abgebrochen, und sein Job schlägt fehl. Exporte, die seit ihrem Start (`StartedAt`) länger als diese Zeit plus fünf Minuten laufen (ein Server wurde beim Schreiben beendet), und Exporte, die einen Tag warten, ohne zu starten, schlagen fehl: Der Job `coworkee-stale-exports` räumt sie alle fünf Minuten auf, die eigenen eines Benutzers auch, wenn er den nächsten Export startet, so dass keiner für immer läuft. Die Zeit in der Warteschlange zählt nicht zur Zeitgrenze.

Jede Änderung des Status eines Exports gilt nur aus dem Status, in dem er erwartet wird (`Status` ist ein Concurrency-Token): laufend zu wartend (die Übergabe eines großen Streams), wartend zu laufend, laufend zu fertig oder fehlgeschlagen, und laufend oder wartend zu abgebrochen; die Aufräumung liegengebliebener Exporte lässt jeden Job einzeln so scheitern. Verliert eine Änderung, folgt der Export dem, was zuerst geschah: nach einem Abbruch endet er still (der Stream mit `cancelled`), nach der Aufräumung als fehlgeschlagen, und in beiden Fällen wird nichts an die Warteschlange übergeben, geschrieben oder gemailt. Ein abgeschlossener Status wird nie überschrieben, und ein Export-Job führt nur einen noch wartenden Job aus. Der Export-Job mailt erst, nachdem sein Status fertig gespeichert ist; scheitert diese Mail, bleibt der Job fertig und trägt eine Meldung dazu.

### Gemailte und geplante Exporte

`POST /api/v1/data/{set}/export/mail` mit `MailExportRequest(Query, Recipients, FileName)` schreibt den Export als Hintergrund-Job und mailt ihn dem Nutzer und den Empfängern (`202` mit dem Job). Andere Empfänger als der Nutzer brauchen `DataTransfer.ExportSchedules`; höchstens 20 Empfänger, jeder eine einfache E-Mail-Adresse, sonst `400`.

Geplante Exporte (`DataTransfer.ExportSchedules` plus die eigene Berechtigung des Sets) mailen eine Liste nach Plan: `ExportSchedule` (Tabelle `cw.ExportSchedules`) speichert das Set, die Abfrage unverändert (`$filter`, `$search`, `$orderby`, `columns`, `format`), einen Namen, einen Dateinamen, weitere Empfänger, die ausgewählten Gruppen und einen `ReportSchedule`: täglich, wöchentlich an einem Wochentag oder monatlich an einem Tag oder am ersten bis vierten oder letzten Wochentag (`Week`), jede n-te Periode (`Every`), zu einer Uhrzeit einer Zeitzone. Der Job `coworkee-export-schedules` (alle fünf Minuten) führt jeden fälligen Plan als sein Besitzer aus, so dass dessen Berechtigungen, Zeilenfilter und Gruppen bestimmen, was die Datei enthält; jeder Lauf ist ein Job des Besitzers mit der Datei, Fehler bleiben am Plan (`LastError`). Ein Lauf sendet nichts mehr, sobald der Besitzer deaktiviert ist oder `DataTransfer.ExportSchedules` oder die Berechtigung des Sets verloren hat; der Grund steht in `LastError` des Plans. Ein Plan oder Hintergrund-Export, der mit ausgewählten Gruppen gespeichert wurde, listet nur die, auf die der Besitzer noch Zugriff hat; ist keine mehr übrig, schlägt er fehl, statt alle Gruppen zu listen.

| Aufruf | Zweck |
|---|---|
| `GET /api/v1/data/export-schedules?entitySet=` | die Pläne des Nutzers |
| `POST /api/v1/data/export-schedules`, `PUT .../{id}` | einen anlegen oder ändern (`SaveExportScheduleRequest`); die Abfrage durchläuft sofort die Abfrageprüfung des Sets (lesbare Eigenschaften, Navigationen, keine Parameter-Aliase): 403 oder 400 |
| `DELETE /api/v1/data/export-schedules/{id}` | einen löschen |
| `POST /api/v1/data/export-schedules/{id}/run` | jetzt senden; der Plan bleibt |

Das Export-Menü einer Tabelle bietet Inhabern der Berechtigung *Export planen*; die Seite *Geplante Exporte* (`/data/export-schedules`, Schlüssel `export-schedules`) listet sie mit *Jetzt senden*, Bearbeiten und Löschen. Die Mail ist die Vorlage `Data.Export` (Mailing-Modul, 23 Sprachen); Dateien über 10 MB werden nicht angehängt, die Mail verweist dann auf die App. Ohne Mailing-Modul wird nichts gemailt (`IExportMailer`). App-Werte wie eine Währung bleiben in der Abfrage; die App liest sie selbst.

## HTTP-API

| Aufruf | Zweck |
|---|---|
| `GET /api/v1/data/imports`, `GET /api/v1/data/{set}/import` | die importierbaren Sets mit ihren Feldern, eines davon |
| `POST /api/v1/data/{set}/import/file` | Upload (multipart, Feld `file`), antwortet mit den erkannten Spalten |
| `POST /api/v1/data/imports/{id}/sheet` | Upload mit anderem Trennzeichen, anderer Kodierung oder Kopfzeile neu lesen |
| `POST /api/v1/data/imports/{id}/preview` | die ersten Zeilen mit `ImportSettings` prüfen |
| `POST /api/v1/data/imports/{id}/start` | Import oder Probelauf (`dryRun: true`) als Job starten |
| `POST /api/v1/data/imports/{id}/undo` | den letzten Import rückgängig machen |
| `GET/POST /api/v1/data/{set}/import/profiles`, `DELETE /api/v1/data/import-profiles/{id}` | gespeicherte Zuordnungen |
| `GET /api/v1/data/jobs?entitySet=`, `GET /api/v1/data/jobs/{id}`, `GET /api/v1/data/jobs/{id}/file` | die Jobs des Nutzers und ihre Dateien |
| `GET /api/v1/data/{set}/export` | der Export: die Datei, oder `202` mit dem Job über `BackgroundExportRows` |
| `GET /api/v1/data/{set}/export/stream`, `GET /api/v1/data/downloads/{token}` | Export mit Fortschrittszeilen, sein einmaliger Download (ohne Anmeldung) |
| `POST /api/v1/data/jobs/{id}/cancel` | einen laufenden oder wartenden Export des Benutzers abbrechen (keine Datei, keine Mail, der Platz wird frei) |
| `POST /api/v1/data/{set}/export/mail` | Export als Job, gemailt |
| `/api/v1/data/export-schedules` | geplante Exporte, siehe [oben](#gemailte-und-geplante-exporte) |
| `POST /api/v1/data/{set}/import` | Import früherer Versionen in einem Schritt: Spalten heißen wie die Felder, nur neue Zeilen |

**Stand:** Uploads und Ergebnisdateien liegen in der Job-Zeile in der Datenbank, das passt für Dateien von wenigen MB. Schlüssel- und Verweissuche stellen eine Abfrage pro Zeile und neuem Text. Rückgängig löscht nur angelegte Zeilen.
