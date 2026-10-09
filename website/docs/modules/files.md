# Files

`Coworkee.Files` keeps folders and files of an organisation in the [blob storage](storage.md): nested folders, upload, download, preview of nearly every file type, move, rename and delete, with per-folder permissions.

```csharp
[DependsOn(typeof(CoworkeeFilesModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

- Page `/files` (`Files.View`) with `MudExFileManager`: folder tree, files as tiles or details, drag and drop to move and to upload. A double-click opens the file in `MudExFileDisplay` in a side sheet.
- `Files.View` reads, `Files.Upload` uploads and creates folders, `Files.Manage` renames, moves and deletes. Uploaders may rename, move and delete their own files.
- Per folder: grant a role on the resource type `FileFolder` (`ResourcePermissionsPanel ResourceType="FileFolder"`); the grant covers all subfolders. The root needs a global permission; without one it lists the folders granted to the user.
- The *Registrations* tree of the registration documents is out of reach of global grants: only `Files.Registrations.View` (administrators) and the grant of each user on his own folder open it. The OData sets leave it out as well.
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

The inline answer carries `Content-Security-Policy: sandbox; default-src 'none'; ...` and `X-Content-Type-Options: nosniff`, so an uploaded HTML or SVG file can be viewed but runs no script and loads nothing.

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

## Database

The tables `cw.FileFolders` and `cw.StoredFiles` come with the model; add a migration in your app.
