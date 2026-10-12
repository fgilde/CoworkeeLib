# MCP-Server der App

Jede Coworkee-App ist ein [MCP](https://modelcontextprotocol.io)-Server: Claude Desktop, Claude Code, VS Code, Cursor und andere MCP-Clients verbinden sich damit und nutzen die Werkzeuge der App als der Benutzer, dem der API-Schlüssel gehört. Es sind dieselben Werkzeuge wie beim [KI-Assistenten](ai.md); für MCP braucht es kein Modell und keinen KI-Anbieter.

- **Transport**: Streamable HTTP, zustandslos, jede Anfrage wird für sich angemeldet.
- **Adresse**: die API stellt ihn unter `/mcp` bereit. Der Web-Host leitet `/mcp` ebenfalls an die API weiter, Clients nutzen also die Adresse der App (`https://myapp.example/mcp`). Die Browser-Sitzung geht dabei nie mit: über den Web-Host erreichen nur API-Schlüssel die Werkzeuge.
- **Wer**: Benutzer mit der Berechtigung `Ai.Chat`. Mit einem [API-Schlüssel](../security/api-keys.md) muss `Ai.Chat` unter den Berechtigungen des Schlüssels sein.
- **Rate-Limit**: die Policy `ai`, gezählt pro Benutzer.

```json
{
  "Coworkee": {
    "Mcp": {
      "Enabled": true,
      "Path": "/mcp"
    }
  }
}
```

`Enabled` ist standardmäßig an; mit `false` gibt es keinen MCP-Endpunkt.

## Werkzeuge

Die Werkzeuge kommen aus denselben Quellen wie die des Assistenten:

- Requests, registriert mit `services.AddAiTool<TRequest>("name", "description")`,
- jeder Request eines geladenen Moduls, der ein `Result` liefert und `[RequiresPermission]` oder `[AiTool]` trägt; `[AiTool(Exclude = true)]` hält einen heraus,
- mit dem OData-Modul `list_data_sets` (die Sets, die der Benutzer lesen darf, mit Feldern und Typen) und `query_data` (Zeilen eines Sets mit `filter`, `orderBy`, `select`, `top` und `skip`, mit den Berechtigungen, Zeilenfiltern und ausgeblendeten Eigenschaften der Tabellen).

`tools/list` nennt nur die Werkzeuge, die der Aufrufer nutzen darf. Queries sind als nur lesend markiert. Commands namens `delete_…`, `remove_…`, `revoke_…`, `purge_…` oder `erase_…` und solche mit `[AiTool(Destructive = true)]` sind als destruktiv markiert, Clients fragen ihren Benutzer, bevor sie sie ausführen.

```csharp
[AiTool("Deletes a product.", Destructive = true)]
[RequiresPermission(CatalogPermissions.Products.Delete)]
public sealed record DropProductCommand(Guid Id) : ICommand<Result>;
```

Jeder Aufruf läuft als der aufrufende Benutzer durch den Dispatcher: Berechtigungen, Organisation, Validierung und Unit of Work gelten wie in der API, und Response-Filter formen das Ergebnis wie die Antworten der API. Jeder Aufruf landet im Audit der KI-Werkzeugaufrufe mit dem Kanal `mcp`, dem Benutzer und dem API-Schlüssel.

## Einen Client verbinden

Unter *Konto > API-Schlüssel* einen Schlüssel anlegen, mit der Berechtigung *Use the assistant and the MCP server* und den Berechtigungen, die die Werkzeuge brauchen. Die Seite zeigt den Schlüssel einmal, zusammen mit der Adresse und einer fertigen Konfiguration.

### Claude Desktop

`claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "myapp": {
      "type": "http",
      "url": "https://myapp.example/mcp",
      "headers": { "Authorization": "Bearer cwk_..." }
    }
  }
}
```

Versionen ohne entfernte Server in der Konfigurationsdatei gehen über `mcp-remote`:

```json
{
  "mcpServers": {
    "myapp": {
      "command": "npx",
      "args": ["mcp-remote", "https://myapp.example/mcp", "--header", "Authorization: Bearer ${MYAPP_KEY}"],
      "env": { "MYAPP_KEY": "cwk_..." }
    }
  }
}
```

### Claude Code

```bash
claude mcp add --transport http myapp https://myapp.example/mcp --header "Authorization: Bearer cwk_..."
```

### VS Code

`.vscode/mcp.json` – VS Code fragt einmal nach dem Schlüssel und speichert ihn:

```json
{
  "servers": {
    "myapp": {
      "type": "http",
      "url": "https://myapp.example/mcp",
      "headers": { "Authorization": "Bearer ${input:myapp-key}" }
    }
  },
  "inputs": [
    { "type": "promptString", "id": "myapp-key", "description": "MyApp API key", "password": true }
  ]
}
```

### Cursor

`~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "myapp": {
      "url": "https://myapp.example/mcp",
      "headers": { "Authorization": "Bearer cwk_..." }
    }
  }
}
```

### MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

Transport *Streamable HTTP*, URL `https://myapp.example/mcp`, Header `Authorization` mit `Bearer cwk_...`.

### C#

```csharp
using ModelContextProtocol.Client;

var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new("Bearer", Environment.GetEnvironmentVariable("MYAPP_KEY"));
await using var client = await McpClient.CreateAsync(new HttpClientTransport(
    new HttpClientTransportOptions { Endpoint = new Uri("https://myapp.example/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http));

foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name}: {tool.Description}");
}

var result = await client.CallToolAsync("query_data", new Dictionary<string, object?> { ["entitySet"] = "Products", ["top"] = 5 });
```

## Sicherheit

- Nur JSON-Bodies erreichen die Werkzeuge, und mit einem Cookie angemeldete Anfragen von fremden Browser-Origins werden abgewiesen: ein Formular einer fremden Seite kann keinen angemeldeten Benutzer benutzen.
- Ein API-Schlüssel ist keine Anmeldung, die der Browser von selbst mitschickt; Anfragen mit Schlüssel dürfen daher von jeder Origin kommen (MCP-Clients im Browser).
- Ein Schlüssel handelt höchstens mit den Berechtigungen seines Benutzers und nur mit denen, die er bekommen hat; siehe [API-Schlüssel](../security/api-keys.md).
- Schlüssel gehören nicht in Repositories: Inputs von VS Code, Umgebungsvariablen oder den Secret-Speicher des Clients nutzen.
