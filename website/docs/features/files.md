---
title: "File storage, file manager and search for .NET"
description: "Blob storage on disk, S3 or Azure, a Blazor file manager with previews and folder permissions, public file links and Elasticsearch search in Coworkee."
---

# Files and search

Product photos, signed delivery notes, the PDF a customer attached at registration: sooner or later an order system or customer portal stores files. Coworkee splits the work into storage, a drive for people, and search.

## Storage that does not care where

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

**Status:** public files are new in version 0.0.1.24 and need a migration in apps that use the Files module. Folders have no public state; each file is published on its own.

Reference: [File storage](../modules/storage.md), [Files](../modules/files.md), [public files](../modules/files.md#public-files), [Search](../modules/search.md).
