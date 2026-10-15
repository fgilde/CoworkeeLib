---
title: "Dateiablage, Dateimanager und Suche für .NET"
description: "Blob-Speicher auf Festplatte, S3 oder Azure, ein Blazor-Dateimanager mit Vorschau und Ordnerrechten, öffentliche Dateilinks und Suche mit Elasticsearch."
---

# Dateien, Blob-Speicher und Suche für .NET-Geschäftsanwendungen

Fast jede Geschäftsanwendung hat irgendwann mit Dateien zu tun: der unterschriebene Lieferschein, das Produktfoto, das PDF, das ein Kunde bei der Registrierung hochgeladen hat. Dem Code, der sie speichert, sollte es egal sein, ob sie in der Entwicklung auf der lokalen Festplatte und in Produktion in einem S3-Bucket landen. Die Menschen, die mit ihnen arbeiten, brauchen ein Laufwerk mit Ordnern, Vorschau und Berechtigungen, keine Liste von Download-Links.

Coworkee teilt das in drei Teile. `Coworkee.Storage` speichert Bytes unter Schlüsseln hinter einer einzigen Schnittstelle. `Coworkee.Files` setzt das Laufwerk einer Organisation darauf, mit Dateimanager, Vorschau für fast jeden Dateityp und Berechtigungen je Ordner. `Coworkee.Search` indexiert, was Sie durchsuchbar machen möchten, mit Elasticsearch dahinter.

## Anbieter für den Blob-Speicher

`IBlobStorage` hat die wenigen Methoden, die Sie brauchen. Der Anbieter ist Konfiguration, der Code bleibt gleich:

```csharp
await storage.PutAsync($"documents/{document.Id}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id}", ct);
await storage.DeleteAsync($"documents/{document.Id}", ct);
```

| `Coworkee:Storage:Provider` | Einstellungen |
|---|---|
| `FileSystem` (Standard) | `FileSystem:Root` |
| `S3` | `ServiceUrl` (auch MinIO und andere S3-Speicher), `Region`, `Bucket`, `AccessKey`, `SecretKey` |
| `AzureBlob` | `ConnectionString`, `Container` |

Für Downloads, die ohne Sitzung funktionieren müssen, etwa ein Link in einer Mail, erzeugt `BlobLinks` signierte Links mit Ablaufzeit. Im Aspire-App-Host teilen sich alle Dienste mit `Coworkee.Storage` den Ordner `.data/blobs`.

## Ein Dateimanager in Blazor

Die Seite *Dateien* zeigt einen Ordnerbaum und die Dateien als Kacheln oder Details. Benutzer ziehen Dateien hinein, um sie hochzuladen, ziehen sie auf einen Ordner, um sie zu verschieben, und öffnen mit Doppelklick eine Vorschau in einem Seitenblatt. Die Vorschau kommt mit fast jedem Dateityp zurecht.

Drei Berechtigungen decken das Grundlegende ab: `Files.View` liest, `Files.Upload` lädt hoch und legt Ordner an, `Files.Manage` benennt um, verschiebt und löscht. Wer eine Datei hochgeladen hat, darf sie auch selbst verwalten. Darüber hinaus lässt sich einer Rolle ein einzelner Ordner freigeben, und die Freigabe gilt für alles darunter. So sieht der Einkauf seinen eigenen Ordner und sonst nichts.

Löschen ist ein Soft Delete, jede Änderung landet im Audit-Log und wird in Echtzeit an andere Browser übertragen. Uploads fließen direkt in den Speicher; die Grenze liegt standardmäßig bei 2 GB pro Datei (`Coworkee:Files:MaxFileSize`). Eine hochgeladene HTML- oder SVG-Datei lässt sich ansehen, führt aber kein Skript aus: Die Antwort trägt eine Sandbox-Content-Security-Policy und `nosniff`.

Uploads können auch aus Google Drive, OneDrive und Dropbox kommen, sobald ein Administrator die Client-ID eines Anbieters in den Einstellungen hinterlegt hat.

## Dateien in Formularen auswählen

`CoworkeeFilePicker` öffnet den Dateimanager in einem Seitenblatt, in dem der Benutzer eine vorhandene Datei auswählt oder eine neue hochlädt:

```razor
<CoworkeeFilePicker @bind-Value="_logoId" Accept="image/*" Label="Logo" />
<CoworkeeFilePicker Multiple="true" @bind-Values="_attachmentIds" />
```

Die Bibliothek nutzt ihn selbst. Der Theme-Editor bietet für das Logo *Aus Dateien wählen* an, und das Design der Anmeldeseite holt sein Hintergrundbild oder -video aus den Dateien der App.

![Die Seite Themes mit der Karte Anmeldeseite: Layouts Card, Split und Slide und das Feld für die Bild- oder Videoadresse](../assets/screenshots/themes-light.webp){ .shot loading=lazy }

## Öffentliche Dateien mit anonymen Links

Eine einzelne Datei lässt sich öffentlich machen. Danach kann jeder sie ohne Anmeldung unter `/api/v1/files/public/{id}/{name}` laden, mit Range-Unterstützung. Die Seite *Dateien* hat dafür *Öffentlich machen*, *Privat machen* und *Öffentlichen Link kopieren* in der Leiste, öffentliche Dateien tragen einen Globus. Weil eine öffentliche Datei höchstens fünf Minuten im Cache liegt, verschwindet eine wieder privat gemachte Datei kurz danach aus den Browsern.

Eine Datei öffentlich zu machen erfordert `Files.Manage` auf ihrem Ordner, auch für den, der sie hochgeladen hat. `PublicFileChooser.ChooseAsync(...)` bündelt den Ablauf für Seiten, die jeder sieht: Er öffnet die Auswahl, fragt nach, bevor er eine private Datei veröffentlicht, und gibt die absolute öffentliche Adresse zurück. So kommt die Anmeldeseite zu ihren Medien, die Auth-Server und Keycloak ohne Sitzung laden können.

## Suche mit Elasticsearch

`Coworkee.Search` beschreibt einen Index, `Coworkee.Search.Elasticsearch` setzt ihn um. Welche Dokumente hineinkommen, entscheidet Ihre App:

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

Abfragen unterstützen Volltext, Filter, Sortierung, Facetten und Cursor-Paging. `CreateIndexAsync` und `SwapAsync` bauen einen Index im Hintergrund neu auf und schalten den Alias ohne Ausfallzeit um. Der Aspire-App-Host startet Elasticsearch nur, wenn ein Dienst das Paket referenziert.

## Weiterlesen

- [Dateiablage](../modules/storage.md): Anbieter und ihre Einstellungen, signierte Links
- [Dateien](../modules/files.md): Berechtigungen, Endpunkte, Vorschau und Cloud-Dateiauswahl
- [Dateien in Formularen auswählen](../modules/files.md#dateien-in-formularen-auswahlen): `CoworkeeFilePicker`, `FilePreviewDialog` und der typisierte Client
- [Öffentliche Dateien](../modules/files.md#offentliche-dateien): Veröffentlichen, Links und `PublicFileChooser`
- [Suche](../modules/search.md): Schema, Abfragen und die Verbindung zu Elasticsearch
- [Anmeldeseiten](../security/sign-in-pages.md): Layouts und Medien des Anmeldedesigns
