# File storage

`IBlobStorage` stores files under keys. The provider is configuration, the code stays the same.

```csharp
await storage.PutAsync($"documents/{document.Id}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id}", ct);
await storage.DeleteAsync($"documents/{document.Id}", ct);
```

| `Coworkee:Storage:Provider` | Settings |
|---|---|
| `FileSystem` (default) | `FileSystem:Root` |
| `S3` | `S3:ServiceUrl` (MinIO and other S3 stores), `Region`, `Bucket`, `AccessKey`, `SecretKey`, `CreateBucket` |
| `AzureBlob` | `AzureBlob:ConnectionString`, `Container` |

`BlobLinks` creates signed, expiring links for downloads that must work without a session, for example in mails. The app host gives every service with `Coworkee.Storage` the same folder, `.data/blobs` next to `src`.
