# File storage

`Coworkee.Storage` (`CoworkeeStorageModule`) stores files under keys through `IBlobStorage`. The provider is configuration, the code stays the same. The [Files](files.md) module, [record attachments](attachments.md), chat files and backups keep their content here.

```csharp
await storage.PutAsync($"documents/{document.Id:N}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id:N}", ct);   // null when missing
await storage.DeleteAsync($"documents/{document.Id:N}", ct);                               // missing blobs are ignored
```

| Member | |
|---|---|
| `PutAsync(key, stream, contentType)` | writes or overwrites a blob |
| `OpenReadAsync(key)` | a seekable stream with a known length, or `null` |
| `ExistsAsync(key)` | whether the blob exists |
| `DeleteAsync(key)` | removes it |
| `DeletePrefixAsync(prefix)` | removes every blob below `prefix/` |

## Keys

A key is up to 512 characters: segments of ASCII letters, digits, `-`, `_` and `.`, separated by single `/`, never `.` or `..`. Every provider checks it and throws an `ArgumentException` otherwise, so a key never leaves its folder. `BlobKeys.New(tenantId, clock)` gives a fresh key `{tenant}/{yyyy}/{MM}/{guid}`, the scheme the Files module uses.

## Providers

| `Coworkee:Storage:Provider` | Settings under `Coworkee:Storage` |
|---|---|
| `FileSystem` (default) | `FileSystem:Root` (`App_Data/blobs`) |
| `S3` | `S3:ServiceUrl` (MinIO and other S3 stores; empty uses AWS with the region), `Region` (`us-east-1`), `Bucket` (`coworkee`), `AccessKey`, `SecretKey`, `CreateBucket` (`true`: creates a missing bucket on the first write; turn it off where the credentials may not create buckets) |
| `AzureBlob` | `AzureBlob:ConnectionString` (`UseDevelopmentStorage=true`), `Container` (`coworkee`) |

Another value stops the start. Every service that reads or writes files needs the same provider and place. In a local run the app host gives every service with `Coworkee.Storage` the same folder, `.data/blobs` next to `src` (`CoworkeeAppOptions.BlobRoot`); a deployment mounts the volume `{app}-files` and sets `FileSystem:Root` to `/home/app/blobs`.

## Links without a session

`BlobLinks` creates signed, expiring tokens for a key, for downloads that must work without a session, for example in mails: `links.Create(key, TimeSpan.FromHours(1))`, and in the endpoint that serves the file `links.TryResolve(token, out var key)`. The tokens are protected with ASP.NET Core Data Protection, so every host that resolves them needs the shared key ring. For files of the Files module and record attachments use their [temporary links](files.md#temporary-links) instead: they check the user's access when they are created.
