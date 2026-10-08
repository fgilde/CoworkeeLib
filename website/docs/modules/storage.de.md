# Dateiablage

`IBlobStorage` speichert Dateien unter Schlüsseln. Der Anbieter ist Konfiguration, der Code bleibt gleich.

```csharp
await storage.PutAsync($"documents/{document.Id}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id}", ct);
await storage.DeleteAsync($"documents/{document.Id}", ct);
```

| `Coworkee:Storage:Provider` | Einstellungen |
|---|---|
| `FileSystem` (Standard) | `FileSystem:Root` |
| `S3` | `S3:ServiceUrl` (MinIO und andere S3-Speicher), `Region`, `Bucket`, `AccessKey`, `SecretKey`, `CreateBucket` |
| `AzureBlob` | `AzureBlob:ConnectionString`, `Container` |

`BlobLinks` erzeugt signierte, ablaufende Links für Downloads, die ohne Sitzung funktionieren müssen, etwa in Mails. Der AppHost gibt jedem Dienst mit `Coworkee.Storage` denselben Ordner, `.data/blobs` neben `src`.
