# Dateiablage

`Coworkee.Storage` (`CoworkeeStorageModule`) speichert Dateien über `IBlobStorage` unter Schlüsseln. Der Anbieter ist Konfiguration, der Code bleibt gleich. Das Modul [Dateien](files.md), [Datensatz-Anhänge](attachments.md), Chat-Dateien und Backups legen ihre Inhalte hier ab.

```csharp
await storage.PutAsync($"documents/{document.Id:N}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id:N}", ct);   // null, wenn sie fehlt
await storage.DeleteAsync($"documents/{document.Id:N}", ct);                               // fehlende Blobs werden ignoriert
```

| Member | |
|---|---|
| `PutAsync(key, stream, contentType)` | schreibt oder überschreibt einen Blob |
| `OpenReadAsync(key)` | ein durchsuchbarer Stream mit bekannter Länge, oder `null` |
| `ExistsAsync(key)` | ob der Blob existiert |
| `DeleteAsync(key)` | entfernt ihn |
| `DeletePrefixAsync(prefix)` | entfernt jeden Blob unter `prefix/` |

## Schlüssel

Ein Schlüssel hat bis zu 512 Zeichen: Segmente aus ASCII-Buchstaben, Ziffern, `-`, `_` und `.`, getrennt durch einzelne `/`, nie `.` oder `..`. Jeder Anbieter prüft ihn und wirft sonst eine `ArgumentException`; ein Schlüssel verlässt also nie seinen Ordner. `BlobKeys.New(tenantId, clock)` liefert einen neuen Schlüssel `{tenant}/{yyyy}/{MM}/{guid}`, das Schema des Dateien-Moduls.

## Anbieter

| `Coworkee:Storage:Provider` | Einstellungen unter `Coworkee:Storage` |
|---|---|
| `FileSystem` (Standard) | `FileSystem:Root` (`App_Data/blobs`) |
| `S3` | `S3:ServiceUrl` (MinIO und andere S3-Speicher; leer nimmt AWS mit der Region), `Region` (`us-east-1`), `Bucket` (`coworkee`), `AccessKey`, `SecretKey`, `CreateBucket` (`true`: legt einen fehlenden Bucket beim ersten Schreiben an; ausschalten, wo die Zugangsdaten keine Buckets anlegen dürfen) |
| `AzureBlob` | `AzureBlob:ConnectionString` (`UseDevelopmentStorage=true`), `Container` (`coworkee`) |

Ein anderer Wert verhindert den Start. Jeder Dienst, der Dateien liest oder schreibt, braucht denselben Anbieter und Ort. In einem lokalen Lauf gibt der AppHost jedem Dienst mit `Coworkee.Storage` denselben Ordner, `.data/blobs` neben `src` (`CoworkeeAppOptions.BlobRoot`); ein Deployment hängt das Volume `{app}-files` ein und setzt `FileSystem:Root` auf `/home/app/blobs`.

## Links ohne Sitzung

`BlobLinks` erzeugt signierte, ablaufende Tokens für einen Schlüssel, für Downloads, die ohne Sitzung funktionieren müssen, etwa in Mails: `links.Create(key, TimeSpan.FromHours(1))`, und im Endpunkt, der die Datei ausliefert, `links.TryResolve(token, out var key)`. Die Tokens sind mit ASP.NET Core Data Protection geschützt; jeder Host, der sie auflöst, braucht also den gemeinsamen Schlüsselring. Für Dateien des Dateien-Moduls und für Datensatz-Anhänge nimm stattdessen deren [temporäre Links](files.md#temporare-links): Sie prüfen beim Erzeugen den Zugriff des Benutzers.
