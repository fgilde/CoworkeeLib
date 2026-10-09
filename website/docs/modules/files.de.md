# Dateien

`Coworkee.Files` verwaltet Ordner und Dateien einer Organisation im [Dateispeicher](storage.md): verschachtelte Ordner, Hochladen, Herunterladen, Vorschau für fast jeden Dateityp, Verschieben, Umbenennen und Löschen, mit Berechtigungen pro Ordner.

```csharp
[DependsOn(typeof(CoworkeeFilesModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

- Seite `/files` (`Files.View`) mit `MudExFileManager`: Ordnerbaum, Dateien als Kacheln oder Liste, Drag-and-drop zum Verschieben und Hochladen. Ein Doppelklick öffnet die Datei in `MudExFileDisplay` in einem Seitenblatt.
- `Files.View` liest, `Files.Upload` lädt hoch und legt Ordner an, `Files.Manage` benennt um, verschiebt und löscht. Wer eine Datei hochgeladen hat, darf sie selbst umbenennen, verschieben und löschen.
- Pro Ordner: eine Rolle auf den Ressourcentyp `FileFolder` vergeben (`ResourcePermissionsPanel ResourceType="FileFolder"`); die Freigabe gilt für alle Unterordner. Die oberste Ebene braucht eine globale Berechtigung; ohne sie listet sie die für den Benutzer freigegebenen Ordner.
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

Die Inline-Antwort trägt `Content-Security-Policy: sandbox; default-src 'none'; ...` und `X-Content-Type-Options: nosniff`: Eine hochgeladene HTML- oder SVG-Datei lässt sich ansehen, führt aber kein Skript aus und lädt nichts nach.

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

## Datenbank

Die Tabellen `cw.FileFolders` und `cw.StoredFiles` kommen mit dem Modell; legen Sie in Ihrer App eine Migration an.
