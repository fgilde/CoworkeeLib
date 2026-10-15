---
title: "File storage, file manager and search for .NET"
description: "Blob storage on disk, S3 or Azure, a Blazor file manager with previews and folder permissions, public file links and Elasticsearch search in Coworkee."
---

# Files, blob storage and search for .NET business apps

Almost every business app ends up handling files: the signed delivery note, the product photo, the PDF a customer uploaded at registration. The code that stores them should not care whether they land on a local disk in development and in an S3 bucket in production. The people who use them need a drive with folders, previews and permissions, not a list of download links.

Coworkee splits this into three parts. `Coworkee.Storage` stores bytes under keys behind one interface. `Coworkee.Files` puts an organisation's drive on top of it, with a file manager, previews of nearly every file type and per-folder permissions. `Coworkee.Search` indexes what you decide to make searchable, backed by Elasticsearch.

## Blob storage providers

`IBlobStorage` has the few methods you need. The provider is configuration, the code stays the same:

```csharp
await storage.PutAsync($"documents/{document.Id}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id}", ct);
await storage.DeleteAsync($"documents/{document.Id}", ct);
```

| `Coworkee:Storage:Provider` | Settings |
|---|---|
| `FileSystem` (default) | `FileSystem:Root` |
| `S3` | `ServiceUrl` (also MinIO and other S3 stores), `Region`, `Bucket`, `AccessKey`, `SecretKey` |
| `AzureBlob` | `ConnectionString`, `Container` |

For downloads that must work without a session, for example a link in a mail, `BlobLinks` creates signed links that expire. In the Aspire app host every service with `Coworkee.Storage` shares the folder `.data/blobs`.

## A file manager in Blazor

The *Files* page shows a folder tree and the files as tiles or details. Users drag files in to upload, drag them onto a folder to move them, and double-click to open a preview in a side sheet. The preview handles nearly every file type.

Three permissions cover the basics: `Files.View` reads, `Files.Upload` uploads and creates folders, `Files.Manage` renames, moves and deletes. Uploaders may manage their own files. On top of that a role can be granted on a single folder, and the grant covers everything below it, so the purchasing team sees its own folder and nothing else.

Deleting is a soft delete, every change lands in the audit log and is pushed to other browsers in real time. Uploads stream straight to storage; the default limit is 2 GB per file (`Coworkee:Files:MaxFileSize`). An uploaded HTML or SVG file can be viewed but runs no script: the answer carries a sandbox content security policy and `nosniff`.

Uploads can also come from Google Drive, OneDrive and Dropbox once an administrator enters the client id of a provider in the settings.

## Picking files in forms

`CoworkeeFilePicker` opens the file manager in a side sheet, where the user picks an existing file or uploads a new one:

```razor
<CoworkeeFilePicker @bind-Value="_logoId" Accept="image/*" Label="Logo" />
<CoworkeeFilePicker Multiple="true" @bind-Values="_attachmentIds" />
```

The library uses it itself. The theme editor offers *Choose from files* for the logo, and the sign-in page design picks its background image or video from the app's files.

![The Themes page with the sign-in page card: layouts Card, Split and Slide and the image or video address field](../assets/screenshots/themes-light.webp){ .shot loading=lazy }

## Public files with anonymous links

A single file can be made public. Anyone can then load it without signing in, under `/api/v1/files/public/{id}/{name}`, with range support. The *Files* page has *Make public*, *Make private* and *Copy public link* in the bar, and public files carry a globe. Because a public file is cached for at most five minutes, a file made private again disappears from browsers shortly after.

Making a file public needs `Files.Manage` on its folder, also for the uploader. `PublicFileChooser.ChooseAsync(...)` wraps the flow for pages anyone sees: it opens the picker, asks before it publishes a private file and returns the absolute public address. That is how the sign-in page gets its media, which the auth server and Keycloak can load without a session.

## Search with Elasticsearch

`Coworkee.Search` describes an index, `Coworkee.Search.Elasticsearch` implements it. Your app decides which documents go in:

```csharp
var schema = new SearchSchema("myapp-products",
[
    new SearchField("name", SearchFieldType.Text),
    new SearchField("brand", SearchFieldType.Keyword),
    new SearchField("rate", SearchFieldType.Double),
]);

await index.EnsureAsync(schema, ct);
await index.UpsertAsync(schema.Alias, [new SearchDocument(product.Id.ToString(), body)], ct);
```

Queries support full text, filters, sorting, facets and cursor paging. `CreateIndexAsync` and `SwapAsync` rebuild an index in the background and switch the alias without downtime. The Aspire app host starts Elasticsearch only when a service references the package.

## Where to go next

- [File storage](../modules/storage.md): providers and their settings, signed links
- [Files](../modules/files.md): permissions, endpoints, previews and cloud file pickers
- [Choosing files in a form](../modules/files.md#choosing-files-in-a-form): `CoworkeeFilePicker`, `FilePreviewDialog` and the typed client
- [Public files](../modules/files.md#public-files): publishing, links and `PublicFileChooser`
- [Search](../modules/search.md): schema, queries and the Elasticsearch connection
- [Sign-in pages](../security/sign-in-pages.md): layouts and media of the sign-in design
