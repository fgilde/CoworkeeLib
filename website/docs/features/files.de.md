---
title: "Dateiablage, Dateimanager und Suche für .NET"
description: "Blob-Speicher auf Festplatte, S3 oder Azure, ein Blazor-Dateimanager mit Vorschau und Ordnerrechten, öffentliche Dateilinks und Suche mit Elasticsearch."
---

# Dateien und Suche

`IBlobStorage` legt Bytes unter einem Schlüssel ab, öffnet und löscht sie. Ob sie in einem lokalen Ordner, einem S3-Bucket (MinIO geht auch) oder in Azure Blob Storage landen, entscheidet eine einzige Einstellung, `Coworkee:Storage:Provider`:

```csharp
await storage.PutAsync($"documents/{document.Id}", stream, contentType, ct);
await using var content = await storage.OpenReadAsync($"documents/{document.Id}", ct);
```

Für einen Download-Link in einer Mail signiert `BlobLinks` einen Link mit Ablaufzeit.

## Die Seite Dateien

Das Files-Modul setzt das Laufwerk einer Organisation obendrauf: Ordnerbaum, Kacheln oder Details, Hochladen und Verschieben per Drag and Drop, ein Doppelklick öffnet die Vorschau in einem Seitenblatt. Rollen lassen sich pro Ordner berechtigen, die Freigabe gilt für alles darunter. Uploads fließen direkt in die Ablage, standardmäßig bis 2 GB pro Datei. Eine hochgeladene HTML- oder SVG-Datei kannst du ansehen, Skripte führt sie aber nicht aus.

In Formularen wählst du Dateien mit `CoworkeeFilePicker`:

```razor
<CoworkeeFilePicker @bind-Value="_logoId" Accept="image/*" Label="Logo" />
```

Die Bibliothek nutzt ihn selbst. Der Theme-Editor holt sein Logo aus den Dateien, die Anmeldeseite ihr Hintergrundbild oder -video.

![Die Seite Themes mit der Karte Anmeldeseite: Layouts Card, Split und Slide und das Feld für die Bild- oder Videoadresse](../assets/screenshots/themes-light.webp){ .shot loading=lazy }

Diese Medien müssen ohne Sitzung laden, deshalb kommen sie aus einer öffentlichen Datei. Jede einzelne Datei kannst du öffentlich machen (*Öffentlich machen*, dann *Öffentlichen Link kopieren*); sie ist danach unter `/api/v1/files/public/{id}/{name}` für jeden abrufbar. Browser cachen sie höchstens fünf Minuten, machst du sie wieder privat, greift das also kurz danach. `PublicFileChooser.ChooseAsync(...)` erledigt Auswahl, Rückfrage und Veröffentlichen in einem Aufruf.

## Suche

`Coworkee.Search` beschreibt einen Index, das Elasticsearch-Paket setzt ihn um, und deine App entscheidet, was hineinkommt:

```csharp
var schema = new SearchSchema("myapp-products",
[
    new SearchField("name", SearchFieldType.Text),
    new SearchField("brand", SearchFieldType.Keyword),
]);
await index.EnsureAsync(schema, ct);
```

Volltext, Filter, Facetten und Cursor-Paging sind dabei. `CreateIndexAsync` und `SwapAsync` bauen einen Index im Hintergrund neu auf und schalten den Alias ohne Ausfallzeit um.

**Stand:** Öffentliche Dateien sind neu in [0.0.1.24](../reference/changelog.md) und brauchen in Apps mit dem Files-Modul eine Migration. Ordner kennen keinen öffentlichen Zustand; jede Datei wird einzeln veröffentlicht.

Referenz: [Dateiablage](../modules/storage.md), [Dateien](../modules/files.md), [öffentliche Dateien](../modules/files.md#offentliche-dateien), [Suche](../modules/search.md).
