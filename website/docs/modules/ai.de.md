# KI-Assistent und MCP

`Coworkee.Ai` lässt Claude mit der App über dieselben Requests arbeiten, die auch die Oberfläche nutzt. Jedes Werkzeug ist ein Dispatcher-Request; Berechtigungen, Validierung und Audit gelten für den Assistenten genau wie für einen Menschen.

```csharp
services.AddAiTool<SearchAssetsQuery>("search_assets", "Finds assets by text, folder and tags.");
services.AddAiTool<TagAssetsCommand>("tag_assets", "Adds tags to assets.");
```

Auch ohne Registrierung wird jeder Request eines Moduls zum Werkzeug, der ein `Result` liefert und `[RequiresPermission]` oder `[AiTool]` trägt. Seine `[Description]` ist die Werkzeugbeschreibung; `[AiTool(Exclude = true)]` hält einen Request heraus, zum Beispiel einen, der Berechtigungen ändert:

```csharp
[RequiresPermission(CatalogPermissions.Brands.View)]
[Description("Lists brands, optionally filtered by name.")]
public sealed record GetBrandsQuery(string? Search) : IQuery<Result<IReadOnlyList<BrandDto>>>;
```

Der Request-Typ wird zum Eingabeschema des Werkzeugs, das `Result` zu seiner Ausgabe. Fehler erreichen das Modell als Meldungen, auf die es reagieren kann.

- **Assistent-Seite** für Benutzer mit `Ai.Chat`: ein Chat mit den Werkzeugen aller Module, als angemeldeter Benutzer.
- **MCP-Server** unter `/mcp` mit denselben Werkzeugen, für Claude Desktop, Claude Code und andere MCP-Clients; er verlangt einen angemeldeten Benutzer.
- **Werkzeug-Audit** für Benutzer mit `Ai.Audit`: jeder Aufruf mit Eingabe, Ergebnis und Benutzer.
- Commands, die Daten ändern, laufen nur in der ersten Runde einer Antwort. Ein Werkzeugergebnis kann das Modell so nicht dazu bringen, eigenmächtig etwas zu ändern.

| Einstellung | |
|---|---|
| `Ai.Enabled` | an oder aus |
| `Ai.ApiKey` | Anthropic-API-Schlüssel (geheim) |
| `Ai.Model` | Standard `claude-opus-5-5` |
| `Ai.MaxToolRounds` | wie viele Werkzeugrunden eine Antwort dauern darf |
