# Files

`Coworkee.Files` keeps folders and files of an organisation in the [blob storage](storage.md): nested folders, upload, download, preview of nearly every file type, move, rename and delete, with per-folder permissions.

Files is the organisation's drive. The *Documents* sample of the app template is something else: a list of typed records, each with an owner and private or public. An app can have both side by side; code that copies a stored file elsewhere reads it with `OpenFile` as the current user, so the folder permissions apply.

```csharp
[DependsOn(typeof(CoworkeeFilesModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

- Page `/files` (`Files.View`) with `MudExFileManager`: folder tree, files as tiles or details, drag and drop to move and to upload. A double-click opens the file in `MudExFileDisplay` in a side sheet.
- `Files.View` reads, `Files.Upload` uploads and creates folders, `Files.Manage` renames, moves and deletes. Uploaders may rename, move and delete their own files.
- Per folder: grant a role on the resource type `FileFolder` (`ResourcePermissionsPanel ResourceType="FileFolder"`); the grant covers all subfolders. The root needs a global permission; without one it lists the folders granted to the user.
- The *Registrations* tree of the registration documents is out of reach of global grants: only `Files.Registrations.View` (administrators) and the grant of each user on his own folder open it. Users with `Files.View` see *Registrations* at the root with only their own folder in it. The OData sets leave the tree out.
- Deleting is a soft delete of the folder with everything below it; the blobs stay. Every change lands in the audit log and is pushed on the realtime topics `type:StoredFile` and `type:FileFolder`.
- OData sets `StoredFiles` and `FileFolders` (`Files.View`) for search and filters.
- Uploads are the raw request body, streamed to a temp file and then into the storage. The limit is `Coworkee:Files:MaxFileSize` (bytes, default 2 GB).

| Endpoint | |
|---|---|
| `GET /api/v1/files/folders/content?folderId=` | subfolders and files, plus `canUpload` and `canManage` for the folder |
| `POST /api/v1/files/folders` | create `{ "parentId": null, "name": "Docs" }` |
| `PUT /api/v1/files/folders/{id}/name` | rename a folder |
| `POST /api/v1/files?name=&folderId=` | upload: body is the file, `Content-Type` its type |
| `GET /api/v1/files/{id}` | metadata: name, size, type, owner, created |
| `PUT /api/v1/files/{id}/name` | rename a file |
| `POST /api/v1/files/move` | `{ "fileIds": [], "folderIds": [], "targetFolderId": null }` |
| `POST /api/v1/files/delete` | `{ "fileIds": [], "folderIds": [] }` |
| `GET /api/v1/files/{id}/content` | inline, `?download=true` as attachment |

The inline answer carries `Content-Security-Policy: sandbox; default-src 'none'; ...` and `X-Content-Type-Options: nosniff`, so an uploaded HTML or SVG file can be viewed but runs no script and loads nothing. Endpoints of your own that serve uploaded content return the same answer with `BlobContentResult` (`Coworkee.AspNetCore.Http`):

```csharp
return new BlobContentResult(content.Stream, content.ContentType, content.FileName, download: download == true);
```

It streams with range support, sets the sandbox policy when inline and `nosniff` plus `Cache-Control: private, no-store` always.

## Choosing files in a form

```razor
<CoworkeeFilePicker @bind-Value="_logoId" Accept="image/*" Label="Logo" />
<CoworkeeFilePicker Multiple="true" @bind-Values="_attachmentIds" />
```

The button opens a side sheet with the file manager, where the user picks or uploads files. As `MudExObjectEdit` editor:

```csharp
meta.Property(p => p.LogoId).RenderWith<CoworkeeFilePicker, Guid?>(p => p.Value)
    .WithAdditionalAttribute(nameof(CoworkeeFilePicker.Accept), "image/*");
```

`IFilesApi` is the typed client, `FileUrls.FileUrl(navigation, id)` the absolute address for `MudExFileDisplay` (it loads with its own `HttpClient`, a relative address does not work).

`FilePreviewDialog.ShowAsync(dialogs, file)` previews a stored file in a side sheet; `FilePreviewDialog.ShowAsync(dialogs, url, contentType, name)` previews any content address of the app, for example the documents of your own module (a relative address is made absolute).

## Cloud file pickers

Every `MudExUploadEdit` of the app can also take files from Google Drive, OneDrive and Dropbox: the upload of the file manager (Files page and the side sheet of `CoworkeeFilePicker`), the document upload of the template and the upload fields of your own pages. A picker shows as soon as it has its client id or app key; without one nothing changes.

Administrators enter the ids under **Settings**, tab **System** (system organisation only), section **Cloud file pickers**. Each provider has a switch, so a picker can be turned off without losing its id. Defaults come from configuration like every setting:

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

The ids reach every signed in browser with the client settings and are applied when the app loads; a changed id applies after a reload. They identify the app at the provider and are public by nature; no client secret is needed or stored.

| Provider | Where | Steps |
|---|---|---|
| Google Drive | [Google Cloud console](https://console.cloud.google.com/apis/credentials) | enable the [Google Picker API](https://console.cloud.google.com/apis/library/picker.googleapis.com) and the Google Drive API, set up the OAuth consent screen, then **Create credentials > OAuth client ID** of type *Web application* with the address of the app (`https://localhost:7300` in development) as authorised JavaScript origin. The client id ends with `.apps.googleusercontent.com`. [Picker guide](https://developers.google.com/drive/picker/guides/overview) |
| OneDrive | [Microsoft Entra app registrations](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) | **New registration** for accounts in any organisation and personal Microsoft accounts, platform *Single-page application* with the address of the app as redirect URI (the picker returns to the origin of the page). The *Application (client) ID* is the id. [File picker guide](https://learn.microsoft.com/onedrive/developer/controls/file-pickers/) |
| Dropbox | [Dropbox App Console](https://www.dropbox.com/developers/apps) | **Create app** with scoped access, then add the domain of the app (`localhost` in development) under *Chooser / Saver / Embedder domains*. The *App key* is the key. [Chooser guide](https://www.dropbox.com/developers/chooser) |

The pickers load their scripts from the providers (`apis.google.com`, `accounts.google.com`, `js.live.net`, `login.microsoftonline.com`, `onedrive.live.com`, `www.dropbox.com`). The web app sets no content security policy; if you add one, allow these hosts for scripts and frames.

## Database

The tables `cw.FileFolders` and `cw.StoredFiles` come with the model; add a migration in your app.
