---
title: "File storage, file manager and search for .NET"
description: "Blob storage on disk, S3 or Azure, a Blazor file manager with previews and folder permissions, public file links and Elasticsearch search in Coworkee."
---

# Files and search

`IBlobStorage` puts, opens and deletes bytes under a key. Whether they land in a local folder, an S3 bucket (MinIO works too) or Azure Blob Storage is a single setting, `Coworkee:Storage:Provider`:

```csharp
await storage.PutAsync($"documents/{document.Id}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id}", ct);
```

For a download link in a mail, `BlobLinks` signs one that expires.

## The Files page

The Files module puts an organisation's drive on top: folder tree, tiles or details, drag and drop to upload and move, a double-click opens the preview in a side sheet. Roles can be granted per folder, and the grant covers everything below it. Uploads stream to storage, up to 2 GB per file by default. An uploaded HTML or SVG file can be looked at but runs no script.

Forms pick files with `CoworkeeFilePicker`:

```razor
<CoworkeeFilePicker @bind-Value="_logoId" Accept="image/*" Label="Logo" />
```

The library uses it too. The theme editor takes its logo from the files, and the sign-in page its background image or video.

![The Themes page with the sign-in page card: layouts Card, Split and Slide and the image or video address field](../assets/screenshots/themes-light.webp){ .shot loading=lazy }

That sign-in media has to load without a session, so it comes from a public file. Any single file can be made public (*Make public*, then *Copy public link*) and is then served under `/api/v1/files/public/{id}/{name}` to anyone. Browsers cache it for five minutes at most, so making it private again takes effect shortly after. `PublicFileChooser.ChooseAsync(...)` does the picking, the question and the publishing in one call.

A private file can be handed out for a moment instead: *Copy temporary link* (`POST /api/v1/files/{id}/token`) gives a link that opens it without sign-in for five minutes ([temporary links](../modules/files.md#temporary-links)).

## Files on a record

[Record attachments](../modules/attachments.md) put files on a row of your own entity, in named slots (`services.AddRecordAttachments<LeaveRequest>("LeaveRequests", a => a.Slot("certificate", ...))`). Who may read them follows the row's visibility in its OData set; the `RecordAttachment` component uploads, shows and deletes them.

## Search

`Coworkee.Search` describes an index, the Elasticsearch package implements it, and your app decides what goes in:

```csharp
var schema = new SearchSchema("myapp-products",
[
    new SearchField("name", SearchFieldType.Text),
    new SearchField("brand", SearchFieldType.Keyword),
]);
await index.EnsureAsync(schema, ct);
```

Full text, filters, facets and cursor paging are there. `CreateIndexAsync` and `SwapAsync` rebuild an index in the background and switch the alias without downtime.

The [global search](../web/global-search.md) needs no index: one search over users, roles, groups, files, reports, chats and the sets you register with `AddGlobalSearch<T>`, under the same permissions and row rules as the lists. The command palette and `/search` use it; an Elasticsearch alias joins it with `AddGlobalSearchIndex`.

**Status:** public files came in [0.0.1.24](../reference/changelog.md) and need a migration in apps that use the Files module. Folders have no public state; each file is published on its own.

Reference: [File storage](../modules/storage.md), [Files](../modules/files.md), [public files](../modules/files.md#public-files), [Record attachments](../modules/attachments.md), [Search](../modules/search.md), [Global search](../web/global-search.md).
