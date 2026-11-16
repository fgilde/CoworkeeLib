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

## Virenscan

Jeder Upload wird geprüft, bevor er gespeichert wird: die [Dateien](files.md), [Datensatz-Anhänge](attachments.md), Dateien im [Chat](chat.md) und die Dokumente der [Registrierung](../security/registration.md). Sie rufen `IVirusScanner` aus `Coworkee.Storage` (registriert von `CoworkeeStorageModule` und dem Auth-Server); Funktionen der App, die Dateien annehmen, rufen ihn ebenso:

```csharp
internal sealed class ImportInvoiceHandler(IVirusScanner scanner, IBlobStorage storage, ICurrentUser user, TimeProvider clock) : IHandler<ImportInvoice, Result>
{
    public async Task<Result> HandleAsync(ImportInvoice command, CancellationToken ct)
    {
        var scan = await scanner.ScanAsync(command.Content, command.FileName, ct);
        if (scan.RefusalFor(command.FileName) is { } refusal)
        {
            return Error.Validation("content", refusal);   // 400 mit dem Grund
        }

        command.Content.Position = 0;
        await storage.PutAsync(BlobKeys.New(user.TenantId!.Value, clock), command.Content, command.ContentType, ct);
        return Result.Success();
    }
}
```

Der Standard, `ClamAvVirusScanner`, schickt den Inhalt an clamd (`INSTREAM` über TCP) unter dem Connection String `antivirus`:

| `ConnectionStrings:antivirus` | Der Upload |
|---|---|
| nicht gesetzt | wird ungeprüft gespeichert (`NotScanned`); der erste Upload loggt auf Information, dass kein Scanner eingerichtet ist |
| gesetzt, clamd antwortet `OK` | wird gespeichert (`Clean`) |
| gesetzt, clamd findet etwas | wird mit dem Fund abgelehnt: *The file 'invoice.pdf' was refused: the virus scan found Eicar-Test-Signature.* (`Infected`, als Warnung geloggt) |
| gesetzt, clamd nicht erreichbar, ein Fehler (eine Datei über clamds Grenze) oder eine andere Antwort | wird abgelehnt: *The file 'invoice.pdf' could not be checked for viruses and was not stored. Try again in a few minutes.* (`Failed`, als Fehler geloggt) |

Die Adresse ist `tcp://host:3310`; `host:port` und ein Host allein (Port 3310) gehen auch. Eine App, die keinen Scan verlangt, läuft ohne ClamAV weiter; eine App, die ihn verlangt, speichert nie eine ungeprüfte Datei. Die Endpunkte antworten 400 mit der Meldung, die Registrierung zeigt sie am Dokument in der Sprache des Benutzers.

`VirusScanResult` hat `Outcome` (`Clean`, `NotScanned`, `Infected`, `Failed`) und `Detail` (den Fund, bei `Failed` den Grund fürs Log); `Refused` ist bei `Infected` und `Failed` wahr, `RefusalFor(fileName)` ist die Meldung für den Benutzer oder `null`.

### ClamAV im AppHost

`options.UseAntivirus()` fügt ClamAV hinzu (`coworkee add antivirus` schreibt die Zeile in eine bestehende App, `--antivirus` und die Studio-Karte *Virenscan* in eine neue):

```csharp
var app = builder.AddCoworkeeApp("myapp", options => options.UseAntivirus());
```

- Ein Container `clamav` (`clamav/clamav:1.4`, clamd auf TCP 3310) läuft lokal und geht in jedes Deployment mit (`coworkee deploy`).
- Jeder Dienst mit `Coworkee.Storage` (API, Auth-Server, Worker; nicht die Migrationen) bekommt `ConnectionStrings:antivirus`. Keiner wartet auf ClamAV.
- clamd nimmt Streams bis 2 GB an, die Grenze des Dateien-Moduls (`CLAMD_CONF_StreamMaxLength=2048M`; sein Standard von 25 MB lehnte größere Dateien ab), und lädt seine Signaturen ohne zweite Kopie im Speicher neu (`CLAMD_CONF_ConcurrentDatabaseReload=no`).
- Lokal prüft er mit den Signaturen des Images und lässt deren Download aus (`CLAMAV_NO_FRESHCLAMD=true`); ein Deployment aktualisiert sie bei jedem Start.
- Auf Azure Container Apps (`coworkee deploy azure`) bekommt er 1,5 CPU und 3 GiB (das Standard-GiB reicht für die Signaturen nicht), ein Replikat und TCP-Ingress nur innerhalb der Umgebung; die Dienste erreichen ihn als `tcp://clamav:3310`.

Nach einem Start lädt clamd ein bis zwei Minuten seine Signaturen und antwortet nicht; Uploads in dieser Zeit werden als *nicht prüfbar* abgelehnt, alles andere läuft. Weitere Einstellungen nimmt der Container so: `options.UseAntivirus(clamav => clamav.WithEnvironment("CLAMD_CONF_MaxScanSize", "4000M"))`. Ohne `AddCoworkeeApp` fügt `builder.AddClamAv()` denselben Container hinzu, und `service.WithReference(clamav, "antivirus")` gibt ihn einem Dienst.

Ein eigener clamd statt des Containers, auf einem anderen Host oder als verwalteter Dienst:

```csharp
// ConnectionStrings:antivirus des AppHosts, z. B. tcp://av.example.com:3310
options.UseAntivirus(builder.AddConnectionString("antivirus"));
```

Ohne AppHost ist es Konfiguration jedes Hosts, der Uploads annimmt (API und Auth-Server):

```json
{ "ConnectionStrings": { "antivirus": "tcp://clamav.internal:3310" } }
```

### Auf der Seite Dienste

Mit `ConnectionStrings:antivirus` hat der Host den Health Check `antivirus` (clamds `VERSION`). Die Admin-Seite **Dienste** zeigt ihn als Kachel *antivirus*: *Läuft* mit der ClamAV-Version und dem Datum seiner Signaturen, *Eingeschränkt* mit dem Grund, solange clamd nicht antwortet, auch während er seine Signaturen lädt. Er ist nie *Gestört*: `/health` der API bleibt oben, weder Aspire noch Container Apps halten die API wegen ClamAV zurück oder starten sie neu. Eigene Health Checks erscheinen dort ebenso, wenn sie das Tag `CoworkeeServicesOptions.HealthCheckTag` tragen, siehe [Einstellungen](settings.md#dienste).

### Ein eigener Scanner

Registriere einen eigenen `IVirusScanner`; er ersetzt den Standard, egal ob er vor oder nach den Modulen registriert wird:

```csharp
[RegisterAs(typeof(IVirusScanner), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class CloudVirusScanner(IHttpClientFactory clients) : IVirusScanner
{
    public async Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var response = await clients.CreateClient("scan").PostAsync("https://scan.example.com/v1/scan", new StreamContent(content), ct);
        if (!response.IsSuccessStatusCode)
        {
            return VirusScanResult.Failed($"the scan service answered {(int)response.StatusCode}");
        }

        var verdict = (await response.Content.ReadFromJsonAsync<Verdict>(ct))!;
        return verdict.Infected ? VirusScanResult.Infected(verdict.Name) : VirusScanResult.Clean;
    }

    private sealed record Verdict(bool Infected, string Name);
}
```

```csharp
// oder in einem Modul, das Registrierte ersetzend
context.Services.Replace(ServiceDescriptor.Singleton<IVirusScanner, CloudVirusScanner>());
```

Der Auth-Server prüft Registrierungsdokumente mit dem `IVirusScanner` seines eigenen Hosts; ein eigener Scanner gehört also auch in den Auth-Host. `NotScanned` lässt eine Datei ungeprüft durch, `Failed` lehnt sie als nicht geprüft ab. Weitere Prüfungen des Inhalts bleiben `IAttachmentScanner` ([Datensatz-Anhänge](attachments.md#im-app-code), auch für die Dateien) und `IChatAttachmentScanner` ([Chat](chat.md)); sie laufen nach dem Virenscan.
