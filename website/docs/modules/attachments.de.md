# Anhänge an Datensätzen

`CoworkeeAttachmentsModule` (Paket `Coworkee.Files`) hängt Dateien in benannten Slots an Datensätze: die Bescheinigung eines Urlaubsantrags, den Lebenslauf eines Bewerbers, die Dokumente eines Mitarbeiters. Die Metadaten liegen in der Tabelle `cw.RecordAttachments`, der Inhalt im [Blob-Speicher](storage.md). Es ist unabhängig von der [Dateiablage](files.md): keine Ordner, keine Ordnerfreigaben, der Datensatz entscheidet, wer seine Dateien sieht.

```csharp
[DependsOn(typeof(CoworkeeAttachmentsModule))]
public sealed class MyAppAbsencesModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddRecordAttachments<LeaveRequest>("LeaveRequests", a => a
            .Slot("certificate", 10 * 1024 * 1024, "application/pdf", "image/*")
            .MultiSlot("documents")
            .WriteRequires(AbsencesPermissions.LeaveRequests.Create));
}
```

Das Modul bringt die Tabelle `cw.RecordAttachments` mit: **Migration anlegen**, wenn es in eine App kommt.

| Registrierung | |
|---|---|
| `Slot(name, maxBytes, contentTypes...)` | eine Datei; ein Upload ersetzt sie (der alte Blob wird gelöscht) |
| `MultiSlot(name, maxBytes, contentTypes...)` | beliebig viele Dateien |
| `WriteRequires(permission)` | Uploads und Löschen brauchen diese Berechtigung zusätzlich zum Sehen des Datensatzes |
| `Authorize((services, access, ct) => ...)` | eine zusätzliche Prüfung beim Lesen (`access.Write == false`) und Schreiben; jede Prüfung muss bestehen |

`maxBytes` ist standardmäßig 10 MB, ohne Inhaltstypen wird jeder angenommen; `image/*` nimmt jeden Bildtyp. Der Name in der Adresse (`LeaveRequests`) ist meist der Name des OData-Sets des Typs. Die Entität braucht eine `Guid`-`Id`; ein anderer Schlüssel verhindert den Start.

## Wer die Dateien sieht und ändert

Lesen (Liste, Öffnen, Herunterladen) folgt dem Datensatz: Hat der Typ ein [OData-Set](../web/odata.md), nur Benutzer, die die Zeile darüber sehen (Berechtigung des Sets, Organisation, Soft Delete und jeder Zeilenfilter `IODataEntityFilter<T>`); ohne Set jede Zeile der eigenen Organisation. Alle anderen bekommen 404, als gäbe es den Datensatz nicht. Schreiben verlangt dasselbe und zusätzlich `WriteRequires` und die `Authorize`-Prüfungen, wenn gesetzt; ohne sie darf jeder hochladen und löschen, der den Datensatz sieht.

## API

| Endpunkt | |
|---|---|
| `GET /api/v1/attachments/{type}/{id}` | jeder Slot, den der Benutzer lesen darf, mit Dateien, Grenzen und `canWrite` |
| `GET /api/v1/attachments/{type}/{id}/{slot}` | ein Slot |
| `GET /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}` | der Inhalt, inline in einer Sandbox; `?download=true` als Download; antwortet auf HEAD |
| `POST /api/v1/attachments/{type}/{id}/{slot}?name=cv.pdf` | Upload: der Body ist die Datei, sein `Content-Type` der Typ der Datei |
| `DELETE /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}` | Datei samt Blob löschen |
| `POST /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}/token` | ein temporärer Link, siehe unten |
| `GET /api/v1/attachments/token/{token}/{name}` | die Datei eines temporären Links, ohne Anmeldung (Rate-Limit `tokens`) |

Eine Datei über der Grenze des Slots, eines Typs, den der Slot nicht nimmt, mit leerem oder pfadartigem Namen oder von einem Scanner abgelehnt, ergibt 400.

Ein kurzlebiger anonymer Link auf einen Anhang: `POST /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}/token` (wer den Datensatz lesen darf) liefert ein `FileTokenDto`; seine `Url` (`AttachmentUrls.Token`) liefert die Datei fünf Minuten lang ohne Anmeldung, inline oder mit `?download=true` als Anhang. `IAttachmentsApi.CreateTokenAsync` ruft ihn auf. Siehe [Temporäre Links](files.md#temporare-links).

## Im App-Code

`IRecordAttachments` macht dasselbe ohne Zugriffsprüfung, für Handler, die selbst prüfen:

```csharp
var stored = await attachments.PutAsync("LeaveRequests", request.Id, "certificate", file.Name, file.ContentType, stream, ct);
var list = await attachments.ListAsync("LeaveRequests", request.Id, cancellationToken: ct);
await using var opened = await attachments.OpenAsync(list[0].Id, ct);
await attachments.DeleteAllAsync("LeaveRequests", request.Id, ct);
```

Die Aufrufe gehören wie jede andere Änderung zur Unit of Work des Aufrufers: `PutAsync` schreibt den Blob und fügt die Zeile hinzu, `DeleteAsync`/`DeleteAllAsync` entfernen Zeilen, und die Pipeline speichert nach dem Command (Code außerhalb eines Commands speichert mit `IUnitOfWork`). Lesende Aufrufe sehen nur gespeicherte Zeilen. Schlägt der Command fehl, wirft er oder speichert er nie, werden die Zeilen nicht geschrieben, und die dafür geschriebenen Blobs werden am Ende des Scopes gelöscht; Blobs entfernter oder ersetzter Zeilen werden erst nach dem Commit gelöscht, ein Rollback behält die Dateien also.

Ein Virenscanner (oder jede andere Prüfung des Inhalts) ist ein `IAttachmentScanner`: Er bekommt den Upload mit Name, Typ und Größe und den gepufferten Inhalt und gibt zurück, warum die Datei abgelehnt wird, oder null. Jeder registrierte Scanner läuft, bevor der Blob gespeichert wird.

```csharp
services.AddSingleton<IAttachmentScanner, ClamAvScanner>();
```

## Löschen mit dem Datensatz

Ein weich gelöschter Datensatz behält seine Dateien, eine Wiederherstellung bringt sie also zurück. Ist die Zeile endgültig weg (das [Aufbewahrungs](retention.md)-Ziel `soft-deleted` oder ein hartes Löschen), löscht das Aufbewahrungsziel `orphaned-attachments` ihre Dateien samt Blobs; Dateien, die jünger als ein Tag sind, bleiben, damit die Dateien eines Datensatzes, der gerade entsteht, nicht verschwinden. Apps, die einen Datensatz endgültig löschen und die Dateien sofort loswerden wollen, rufen `DeleteAllAsync`.

## Echtzeit und personenbezogene Daten

Jeder hinzugefügte, ersetzte oder gelöschte Anhang geht nach dem Commit über das Echtzeit-Topic `attachments:{entityType}:{entityId}` (`AttachmentTopics.Record`). Folgen darf, wer einen Slot des Datensatzes lesen darf, dieselbe Prüfung wie beim Listen-Endpunkt.

Der [Export personenbezogener Daten](../security/privacy.md) hat einen Abschnitt `attachments` mit den Dateien, die der Benutzer angehängt hat (Datensatz, Slot, Name, Typ, Größe, Zeit), ohne den Inhalt. Das Löschen des Benutzers behält sie: Wie andere Dateien gehören sie zu den Datensätzen der Organisation, die Id der hochladenden Person zeigt danach auf niemanden.

## Client

`RecordAttachment` zeigt einen Slot: Jede Datei öffnet im Dateibetrachter (`MudExFileDisplay`) oder wird heruntergeladen; Benutzer, die den Slot ändern dürfen, laden mit `MudExUploadEdit` hoch (Typ- und Größengrenzen des Slots) und löschen nach einer Rückfrage. `ReadOnly` blendet Upload und Löschen aus, `Changed` meldet die neue Liste. Die Komponente folgt dem Anhang-Topic des Datensatzes, Dateien, die andere hinzufügen oder löschen, erscheinen also ohne Neuladen.

```razor
<RecordAttachment EntityType="LeaveRequests" EntityId="@request.Id" Slot="certificate" Label="@L["Medical certificate"]" />
```

`IAttachmentsApi` ist die API dahinter; `AttachmentUrls.Content(attachment, download)` ist die Adresse einer Datei.

Grenzen: Uploads werden in eine temporäre Datei gepuffert (für Provider, die eine bekannte Länge brauchen, und für die Scanner); die Größengrenze des Upload-Endpunkts ist die größte Slot-Grenze plus 64 KB, danach prüft jeder Slot seine eigene.
