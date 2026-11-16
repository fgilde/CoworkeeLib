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

## Virus scan

Every upload is checked before it is stored: the [files](files.md), [record attachments](attachments.md), [chat](chat.md) files and the documents of the [registration](../security/registration.md). They call `IVirusScanner` of `Coworkee.Storage` (registered by `CoworkeeStorageModule` and the auth server); app features that take files call it too:

```csharp
internal sealed class ImportInvoiceHandler(IVirusScanner scanner, IBlobStorage storage, ICurrentUser user, TimeProvider clock) : IHandler<ImportInvoice, Result>
{
    public async Task<Result> HandleAsync(ImportInvoice command, CancellationToken ct)
    {
        var scan = await scanner.ScanAsync(command.Content, command.FileName, ct);
        if (scan.RefusalFor(command.FileName) is { } refusal)
        {
            return Error.Validation("content", refusal);   // 400 with the reason
        }

        command.Content.Position = 0;
        await storage.PutAsync(BlobKeys.New(user.TenantId!.Value, clock), command.Content, command.ContentType, ct);
        return Result.Success();
    }
}
```

The default, `ClamAvVirusScanner`, streams the content to clamd (`INSTREAM` over TCP) at the connection string `antivirus`:

| `ConnectionStrings:antivirus` | The upload |
|---|---|
| not set | is stored unscanned (`NotScanned`); the first upload logs at Information that no scanner is configured |
| set, clamd answers `OK` | is stored (`Clean`) |
| set, clamd finds something | is refused with the finding: *The file 'invoice.pdf' was refused: the virus scan found Eicar-Test-Signature.* (`Infected`, logged as warning) |
| set, clamd unreachable, an error (a file over clamd's limit) or any other answer | is refused: *The file 'invoice.pdf' could not be checked for viruses and was not stored. Try again in a few minutes.* (`Failed`, logged as error) |

The address is `tcp://host:3310`; `host:port` and a host alone (port 3310) work too. An app that did not ask for scanning keeps working without ClamAV; an app that asked for it never stores a file unchecked. The endpoints answer 400 with the message, the registration shows it at the document in the user's language.

`VirusScanResult` has `Outcome` (`Clean`, `NotScanned`, `Infected`, `Failed`) and `Detail` (the finding, for `Failed` the reason for the log); `Refused` is true for `Infected` and `Failed`, `RefusalFor(fileName)` is the message for the user or `null`.

### ClamAV in the app host

`options.UseAntivirus()` adds ClamAV (`coworkee add antivirus` writes the line into an existing app, `--antivirus` and the Studio card *Virus scan* into a new one):

```csharp
var app = builder.AddCoworkeeApp("myapp", options => options.UseAntivirus());
```

- A container `clamav` (`clamav/clamav:1.4`, clamd on TCP 3310) runs locally and goes along into every deployment (`coworkee deploy`).
- Every service with `Coworkee.Storage` (api, auth server, workers; not the migrations) gets `ConnectionStrings:antivirus`. None waits for ClamAV.
- clamd takes streams up to 2 GB, the limit of the Files module (`CLAMD_CONF_StreamMaxLength=2048M`; its default of 25 MB refused larger files), and reloads its signatures without a second copy in memory (`CLAMD_CONF_ConcurrentDatabaseReload=no`).
- Locally it scans with the signatures of the image and skips their download (`CLAMAV_NO_FRESHCLAMD=true`); a deployment updates them at every start.
- On Azure Container Apps (`coworkee deploy azure`) it gets 1.5 CPU and 3 GiB (the default 1 GiB is too small for the signatures), one replica and TCP ingress inside the environment only; the services reach it as `tcp://clamav:3310`.

After a start clamd loads its signatures for a minute or two and answers nothing; uploads in that time are refused as *could not be checked*, everything else works. The container takes further settings: `options.UseAntivirus(clamav => clamav.WithEnvironment("CLAMD_CONF_MaxScanSize", "4000M"))`. Without `AddCoworkeeApp`, `builder.AddClamAv()` adds the same container and `service.WithReference(clamav, "antivirus")` hands it to a service.

A clamd of your own instead of the container, on another host or a managed one:

```csharp
// ConnectionStrings:antivirus of the app host, e.g. tcp://av.example.com:3310
options.UseAntivirus(builder.AddConnectionString("antivirus"));
```

Without the app host it is configuration of every host that takes uploads (api and auth server):

```json
{ "ConnectionStrings": { "antivirus": "tcp://clamav.internal:3310" } }
```

### On the Services page

With `ConnectionStrings:antivirus` the host has the health check `antivirus` (clamd's `VERSION`). The admin page **Services** shows it as the tile *antivirus*: *Healthy* with the ClamAV version and the date of its signatures, *Degraded* with the reason while clamd does not answer, also while it loads its signatures. It is never *Unhealthy*: `/health` of the api stays up, so neither Aspire nor Container Apps hold back or restart the api because of ClamAV. Health checks of your own show there too when they carry the tag `CoworkeeServicesOptions.HealthCheckTag`, see [Settings](settings.md#services).

### A scanner of your own

Register your own `IVirusScanner`; it replaces the default, whether it is registered before or after the modules:

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
// or in a module, replacing what is registered
context.Services.Replace(ServiceDescriptor.Singleton<IVirusScanner, CloudVirusScanner>());
```

The auth server scans registration documents with the `IVirusScanner` of its own host, so a scanner of your own belongs into the auth host as well. `NotScanned` lets a file pass unscanned, `Failed` refuses it as not checked. Further checks of the content stay `IAttachmentScanner` ([record attachments](attachments.md#in-app-code), also for the files) and `IChatAttachmentScanner` ([chat](chat.md)); they run after the virus scan.
