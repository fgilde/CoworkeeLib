# Record attachments

`CoworkeeAttachmentsModule` (package `Coworkee.Files`) attaches files to records in named slots: the certificate of a leave request, the CV of an applicant, the documents of an employee. The metadata lives in the table `cw.RecordAttachments`, the content in the [blob storage](storage.md). It is independent of the [file manager](files.md): no folders, no folder grants, the record decides who sees its files.

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

The module brings the table `cw.RecordAttachments`: **add a migration** when you add it to an app.

| Registration | |
|---|---|
| `Slot(name, maxBytes, contentTypes...)` | one file; an upload replaces it (the old blob is deleted) |
| `MultiSlot(name, maxBytes, contentTypes...)` | any number of files |
| `WriteRequires(permission)` | uploads and deletes need this permission on top of seeing the record |
| `Authorize((services, access, ct) => ...)` | an extra check on reads (`access.Write == false`) and writes; every check must pass |

`maxBytes` defaults to 10 MB, no content types take any; `image/*` takes every image type. The name in the address (`LeaveRequests`) is usually the OData set name of the type.

## Who sees and changes the files

Reading (list, open, download) follows the record: when the type has an [OData set](../web/odata.md), only users who see the row through it (the set's permission, the organisation, soft delete and every `IODataEntityFilter<T>` row filter); without a set, every row of the own organisation. Everyone else gets 404, as if the record did not exist. Writing needs the same, plus `WriteRequires` and the `Authorize` checks when set; without them everyone who sees the record may upload and delete.

## API

| Endpoint | |
|---|---|
| `GET /api/v1/attachments/{type}/{id}` | every slot the user may read, with its files, limits and `canWrite` |
| `GET /api/v1/attachments/{type}/{id}/{slot}` | one slot |
| `GET /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}` | the content, inline in a sandbox; `?download=true` as download; answers HEAD |
| `POST /api/v1/attachments/{type}/{id}/{slot}?name=cv.pdf` | upload: the body is the file, its `Content-Type` the file's type |
| `DELETE /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}` | delete the file and its blob |

A file over the slot's limit, of a type the slot does not take, with an empty or path-like name, or refused by a scanner answers 400.

## In app code

`IRecordAttachments` does the same without access checks, for handlers that check access themselves:

```csharp
var stored = await attachments.PutAsync("LeaveRequests", request.Id, "certificate", file.Name, file.ContentType, stream, ct);
var list = await attachments.ListAsync("LeaveRequests", request.Id, cancellationToken: ct);
await using var opened = await attachments.OpenAsync(list[0].Id, ct);
await attachments.DeleteAllAsync("LeaveRequests", request.Id, ct);
```

The calls join the caller's unit of work like any other change: `PutAsync` writes the blob and adds the row, `DeleteAsync`/`DeleteAllAsync` remove rows, and the pipeline saves after the command (code outside a command saves with `IUnitOfWork`). Reads see saved rows only. When the command fails, throws or never saves, the rows are not written and the blobs written for them are deleted at the end of the scope; blobs of removed or replaced rows are deleted only after the commit, so a rollback keeps the files.

A virus scanner (or any other check of the content) is an `IAttachmentScanner`: it gets the upload with name, type and size and the buffered content, and returns why the file is refused, or null. Every registered scanner runs before the blob is stored.

```csharp
services.AddSingleton<IAttachmentScanner, ClamAvScanner>();
```

## Deleting with the record

A soft deleted record keeps its files, so a restore brings them back. Once the row is gone for good (the `soft-deleted` [retention](retention.md) target or a hard delete), the retention target `orphaned-attachments` deletes its files with their blobs; it keeps files younger than a day, so files of a record that is still being created stay. Apps that delete a record for good and want the files gone at once call `DeleteAllAsync`.

## Client

`RecordAttachment` shows one slot: every file opens in the file viewer (`MudExFileDisplay`) or downloads; users who may change the slot upload with `MudExUploadEdit` (type and size limits of the slot) and delete after a confirmation. `ReadOnly` hides upload and delete, `Changed` reports the new list.

```razor
<RecordAttachment EntityType="LeaveRequests" EntityId="@request.Id" Slot="certificate" Label="@L["Medical certificate"]" />
```

`IAttachmentsApi` is the API behind it; `AttachmentUrls.Content(attachment, download)` is the address of one file.

Limits: uploads are buffered to a temporary file (for providers that need a known length and for the scanners); the request size limit of the upload endpoint is the largest slot limit.
