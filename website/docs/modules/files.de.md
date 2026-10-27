# Dateien

`Coworkee.Files` verwaltet Ordner und Dateien einer Organisation im [Dateispeicher](storage.md): verschachtelte Ordner, Hochladen, Herunterladen, Vorschau für fast jeden Dateityp, Verschieben, Umbenennen und Löschen, mit Berechtigungen pro Ordner.

Dateien sind das Laufwerk der Organisation. Das Beispiel *Documents* der App-Vorlage ist etwas anderes: eine Liste typisierter Datensätze, jeder mit Besitzer und privat oder öffentlich. Eine App kann beides nebeneinander haben; Code, der eine gespeicherte Datei anderswohin kopiert, liest sie mit `OpenFile` als aktueller Benutzer, die Ordnerberechtigungen gelten also. Dateien, die zu einem Datensatz gehören (eine Bescheinigung, ein Lebenslauf), sind stattdessen [Anhänge an Datensätzen](attachments.md): keine Ordner, der Datensatz entscheidet, wer sie sieht.

```csharp
[DependsOn(typeof(CoworkeeFilesModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

- Seite `/files` (`Files.View`) mit `MudExFileManager`: Ordnerbaum, Dateien als Kacheln oder Liste, Drag-and-drop zum Verschieben und Hochladen. Ein Doppelklick öffnet die Datei in `MudExFileDisplay` in einem Seitenblatt.
- `Files.View` liest, `Files.Upload` lädt hoch und legt Ordner an, `Files.Manage` benennt um, verschiebt und löscht. Wer eine Datei hochgeladen hat, darf sie selbst umbenennen, verschieben und löschen.
- Pro Ordner: eine Rolle auf den Ressourcentyp `FileFolder` vergeben (`ResourcePermissionsPanel ResourceType="FileFolder"`); die Freigabe gilt für alle Unterordner. Die oberste Ebene braucht eine globale Berechtigung; ohne sie listet sie die für den Benutzer freigegebenen Ordner.
- Der Baum *Registrations* mit den Registrierungsdokumenten liegt außerhalb globaler Berechtigungen: nur `Files.Registrations.View` (Administratoren) und die Freigabe jedes Benutzers auf seinen eigenen Ordner öffnen ihn. Benutzer mit `Files.View` sehen *Registrations* in der Wurzel, darin nur ihren eigenen Ordner. Die OData-Sets lassen den Baum aus.
- Löschen ist ein Soft Delete des Ordners mit allem darunter; die Blobs bleiben erhalten. Jede Änderung landet im Audit-Log und geht über die Realtime-Topics `type:StoredFile` und `type:FileFolder` raus.
- OData-Sets `StoredFiles` und `FileFolders` (`Files.View`) für Suche und Filter.
- Ein Upload ist der rohe Request-Body; er wird in eine temporäre Datei und von dort in den Speicher gestreamt. Die Grenze ist `Coworkee:Files:MaxFileSize` (Bytes, Standard 2 GB).

| Endpunkt | |
|---|---|
| `GET /api/v1/files/folders/content?folderId=` | Unterordner und Dateien, dazu `canUpload` und `canManage` für den Ordner |
| `POST /api/v1/files/folders` | anlegen `{ "parentId": null, "name": "Docs" }` |
| `PUT /api/v1/files/folders/{id}/name` | Ordner umbenennen |
| `POST /api/v1/files?name=&folderId=` | hochladen: der Body ist die Datei, `Content-Type` ihr Typ |
| `GET /api/v1/files/{id}` | Metadaten: Name, Größe, Typ, Besitzer, Erstellungszeit |
| `PUT /api/v1/files/{id}/name` | Datei umbenennen |
| `POST /api/v1/files/move` | `{ "fileIds": [], "folderIds": [], "targetFolderId": null }` |
| `POST /api/v1/files/delete` | `{ "fileIds": [], "folderIds": [] }` |
| `GET /api/v1/files/{id}/content` | inline, mit `?download=true` als Anhang |

Die Inline-Antwort trägt `Content-Security-Policy: sandbox; default-src 'none'; ...` und `X-Content-Type-Options: nosniff`: Eine hochgeladene HTML- oder SVG-Datei lässt sich ansehen, führt aber kein Skript aus und lädt nichts nach. Eigene Endpunkte, die hochgeladene Inhalte ausliefern, geben dieselbe Antwort mit `BlobContentResult` (`Coworkee.AspNetCore.Http`):

```csharp
return new BlobContentResult(content.Stream, content.ContentType, content.FileName, download: download == true);
```

Es streamt mit Range-Unterstützung, setzt inline die Sandbox-Policy und immer `nosniff` sowie `Cache-Control: private, no-store` (öffentliche Dateien: siehe unten).

## Dateien in Formularen auswählen

```razor
<CoworkeeFilePicker @bind-Value="_logoId" Accept="image/*" Label="Logo" />
<CoworkeeFilePicker Multiple="true" @bind-Values="_attachmentIds" />
```

Der Button öffnet ein Seitenblatt mit dem Dateimanager, in dem man Dateien auswählt oder hochlädt. Als Editor in `MudExObjectEdit`:

```csharp
meta.Property(p => p.LogoId).RenderWith<CoworkeeFilePicker, Guid?>(p => p.Value)
    .WithAdditionalAttribute(nameof(CoworkeeFilePicker.Accept), "image/*");
```

`IFilesApi` ist der typisierte Client, `FileUrls.FileUrl(navigation, id)` die absolute Adresse für `MudExFileDisplay` (es lädt mit einem eigenen `HttpClient`, eine relative Adresse funktioniert dort nicht).

`FilePreviewDialog.ShowAsync(dialogs, file)` zeigt eine gespeicherte Datei in einem Seitenblatt; `FilePreviewDialog.ShowAsync(dialogs, url, contentType, name)` zeigt jede Inhaltsadresse der App, zum Beispiel die Dokumente eines eigenen Moduls (eine relative Adresse wird absolut gemacht).

### Dateien anzeigen

Jede Datei, die die Coworkee-Oberfläche zeigt, läuft über MudEx `MudExFileDisplay`, damit jeder Typ seinen Viewer bekommt: Bilder, Video, Audio, PDF, Word, Excel, PowerPoint, OpenDocument, Code, Markdown, CSV, ZIP, E-Mails, 3D-Modelle und mehr. Dateimanager, Dateiauswahl, Chat-Dateien und ihre Uploads öffnen alle `FilePreviewDialog`; ein kleines Bild in einer Liste (ein Chat-Vorschaubild) ist nur der Einstieg, ein Klick öffnet den Viewer. Mach es in deinen eigenen Seiten genauso: kein eigenes `<img>`, `<iframe>` oder Download-Link für eine gespeicherte Datei, sondern `FilePreviewDialog.ShowAsync`.

Der Viewer lädt die Datei von der eigenen Adresse der App mit dem Sitzungs-Cookie, also gelten die Rechte des Endpunkts (Chat-Dateien nur für die Mitglieder). Private Dateien gehen nie an den Online-Office-Viewer von Microsoft; nur öffentliche Dateien dürfen ihn nutzen.

Manche Viewer laden ihr Skript beim ersten Anzeigen eines Typs von einem CDN (jsDelivr, esm.sh, unpkg), der Karten-Viewer lädt Kacheln von OpenStreetMap. `UseCoworkeeSecurityHeaders` erlaubt sie, siehe [Sicherheits-Header der Web-App](../security/authentication.md#sicherheits-header-der-web-app).

## Öffentliche Dateien

Eine Datei kann öffentlich sein: `PUT /api/v1/files/{id}/public` mit `{ "isPublic": true }` (braucht `Files.Manage` auf ihrem Ordner, auch für den Hochladenden, weil sie danach jeder lesen kann). Öffentliche Dateien werden ohne Anmeldung unter `GET /api/v1/files/public/{id}/{name}` ausgeliefert (`FileLinks.Public(id, name)`, auch über `/api` der Web-App), mit Range-Unterstützung und `Cache-Control: public, max-age=300`, sodass eine wieder private Datei innerhalb von fünf Minuten aus den Browsern verschwindet. `StoredFileDto.IsPublic` zeigt, welche Dateien öffentlich sind; die Vorschau einer öffentlichen Datei sagt es und bietet *Privat machen* an.

Auf der Seite *Dateien* teilt die Leiste über dem Datei-Manager die ausgewählte Datei: *Öffentlich machen* (nach der Rückfrage; nur mit `Files.Manage` auf dem Ordner) oder *Privat machen* und *Öffentlichen Link kopieren*. Öffentliche Dateien tragen im Dateibereich einen Globus und in der Leiste einen Chip *Öffentlich*. Ordner haben keinen öffentlichen Zustand; jede Datei wird einzeln veröffentlicht. `FilePublishing.SetPublicAsync`, `PublicLink` und `CopyPublicLinkAsync` tun dasselbe in eigenen Seiten.

`PublicFileChooser.ChooseAsync(...)` wählt eine Datei für Seiten, die jeder sieht: Es öffnet die Dateiauswahl, fragt nach, bevor es eine private Datei öffentlich macht, und gibt die absolute öffentliche Adresse zurück. Das Anmeldedesign nutzt es für sein Bild oder Video.

## Temporäre Links

Eine Datei, die privat bleibt, lässt sich trotzdem kurz ohne Anmeldung öffnen, etwa von einem Viewer oder einem nativen Download, der kein Cookie mitschickt: `POST /api/v1/files/{id}/token` (wer die Datei öffnen darf) liefert `FileTokenDto(Token, Url, ExpiresAt)`. `GET` oder `HEAD` auf `Url` (`/api/v1/files/token/{token}/{name}`, `FileLinks.Token(token, name)`) liefert die Datei inline, mit `?download=true` als Anhang, fünf Minuten lang und beliebig oft (der Viewer fragt mehrmals). Das Token verrät nichts über die Datei; die Datenbank speichert nur seinen Hash. Auf der Seite *Dateien* kopiert *Temporären Link kopieren* einen solchen Link; `IFilesApi.CreateTokenAsync` und `FilePublishing.CopyTemporaryLinkAsync` tun das in eigenen Seiten.

Die Tokens kommen von `IAccessTokens` (`Coworkee.Infrastructure.Tokens`, Tabelle `cw.AccessTokens`; abgelaufene löscht das Aufbewahrungsziel `access-tokens`): `Issue(purpose, targetId, lifetime, singleUse)` legt ein Token in der Unit of Work an, `RedeemAsync(purpose, token)` liefert das Ziel mit Organisation und Benutzer, die es ausgestellt haben, oder null, wenn das Token unbekannt, abgelaufen oder (einmalig) schon eingelöst ist. Apps nutzen es für eigene anonyme Links.

Die anonymen Routen sind pro Adresse in der Rate begrenzt (Policy `tokens`, 120 pro Minute). Das Token landet nie in Logs oder Traces: Die Anfragezeilen von ASP.NET Core und die Activity der Anfrage (`url.path`, `url.full`, ihr Name) zeigen `/token/***/` und `/downloads/***`; `SecretPaths.Redact` maskiert es in eigenen Logs.

## Cloud-Dateiauswahl

Jedes `MudExUploadEdit` der App kann Dateien auch aus Google Drive, OneDrive und Dropbox übernehmen: der Upload des Dateimanagers (Seite Dateien und das Seitenblatt von `CoworkeeFilePicker`), der Dokument-Upload der Vorlage und die Upload-Felder eigener Seiten. Eine Auswahl erscheint, sobald ihre Client-ID oder ihr App-Key gesetzt ist; ohne bleibt alles wie bisher.

Administratoren tragen die IDs unter **Einstellungen**, Reiter **System** (nur in der Systemorganisation), Abschnitt **Cloud-Dateiauswahl** ein. Jeder Anbieter hat einen Schalter, so lässt sich eine Auswahl abschalten, ohne die ID zu verlieren. Vorgaben kommen wie bei jeder Einstellung aus der Konfiguration:

```json title="appsettings.json"
{
  "Coworkee": {
    "Settings": {
      "Defaults": {
        "FilePickers.GoogleDrive.ClientId": "1234-abc.apps.googleusercontent.com",
        "FilePickers.OneDrive.ClientId": "00000000-0000-0000-0000-000000000000",
        "FilePickers.Dropbox.AppKey": "abcdefghijklmno"
      }
    }
  }
}
```

Die IDs erreichen mit den Client-Einstellungen jeden angemeldeten Browser und gelten ab dem Laden der App; eine geänderte ID gilt nach dem Neuladen. Sie kennzeichnen die App beim Anbieter und sind von Natur aus öffentlich; ein Client-Secret ist weder nötig noch gespeichert.

| Anbieter | Wo | Schritte |
|---|---|---|
| Google Drive | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) | die [Google Picker API](https://console.cloud.google.com/apis/library/picker.googleapis.com) und die Google Drive API aktivieren, den OAuth-Zustimmungsbildschirm einrichten, dann **Anmeldedaten erstellen > OAuth-Client-ID** vom Typ *Webanwendung* mit der Adresse der App (`https://localhost:7300` in der Entwicklung) als autorisiertem JavaScript-Ursprung. Die Client-ID endet auf `.apps.googleusercontent.com`. [Picker-Anleitung](https://developers.google.com/drive/picker/guides/overview) |
| OneDrive | [App-Registrierungen in Microsoft Entra](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) | **Neue Registrierung** für Konten in beliebigen Organisationen und persönliche Microsoft-Konten, Plattform *Single-Page-Anwendung* mit der Adresse der App als Umleitungs-URI (die Auswahl kehrt zum Ursprung der Seite zurück). Die *Anwendungs-ID (Client)* ist die ID. [Anleitung zur Dateiauswahl](https://learn.microsoft.com/onedrive/developer/controls/file-pickers/) |
| Dropbox | [Dropbox App Console](https://www.dropbox.com/developers/apps) | **Create app** mit Scoped Access, dann die Domain der App (`localhost` in der Entwicklung) unter *Chooser / Saver / Embedder domains* eintragen. Der *App key* ist der Key. [Chooser-Anleitung](https://www.dropbox.com/developers/chooser) |

Die Auswahlen laden ihre Skripte von den Anbietern (`apis.google.com`, `accounts.google.com`, `js.live.net`, `login.microsoftonline.com`, `onedrive.live.com`, `www.dropbox.com`). Die Content-Security-Policy von `UseCoworkeeSecurityHeaders` erlaubt keinen davon: Ergänze diese Hosts für Skripte und Frames wie in [Sicherheits-Header der Web-App](../security/authentication.md#sicherheits-header-der-web-app).

## Datenbank

Die Tabellen `cw.FileFolders` und `cw.StoredFiles` kommen mit dem Modell; leg in deiner App eine Migration an.
