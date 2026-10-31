# MCP-Server der App

Jede Coworkee-App ist ein [MCP](https://modelcontextprotocol.io)-Server: Claude Desktop, Claude Code, VS Code, Cursor und andere MCP-Clients verbinden sich damit und nutzen die Werkzeuge der App als der Benutzer, dem der API-Schlüssel gehört. Es sind dieselben Werkzeuge wie beim [KI-Assistenten](ai.md); für MCP braucht es kein Modell und keinen KI-Anbieter.

- **Transport**: Streamable HTTP, zustandslos, jede Anfrage wird für sich angemeldet.
- **Adresse**: die API stellt ihn unter `/mcp` bereit. Der Web-Host leitet ihn ebenfalls weiter, Clients nutzen also die Adresse der App (`https://myapp.example/mcp`). Der Web-Host gibt nur den API-Schlüssel (`Authorization: Bearer cwk_…` oder `X-Api-Key`) und die MCP-Header weiter: nie die Browser-Sitzung, ihre Cookies oder ihr Token, und keinen Pfad mit Punkt-Segmenten oder kodierten Schrägstrichen.
- **Wer**: Benutzer mit der Berechtigung `Ai.Chat`. Mit einem [API-Schlüssel](../security/api-keys.md) muss `Ai.Chat` unter den Berechtigungen des Schlüssels sein. Über den Web-Host funktionieren nur API-Schlüssel; die API selbst nimmt auch das Access Token einer Anmeldung.
- **Rate-Limit**: die Policy `ai` (standardmäßig 30 Anfragen pro Minute und Benutzer, `Coworkee:RateLimiting:Policies:ai`), gemeinsam mit dem Assistenten.

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

`Enabled` ist standardmäßig an; mit `false` gibt es keinen MCP-Endpunkt. Setz beide Werte für die API und den Web-Host, der denselben Pfad weiterleitet.

Zur Laufzeit schalten Host-Administratoren den Server mit der globalen Einstellung `Mcp.Enabled` ab (*MCP-Server aktiv* auf *Administration > System > KI*, `/cw/admin/ai-settings`, standardmäßig an): dann ist `tools/list` leer und `tools/call` führt nichts aus, auch mit gültigen Schlüsseln. Sie hängt nicht an `Ai.Enabled`: MCP braucht kein Modell.

## Werkzeuge

Die Werkzeuge kommen aus denselben Quellen wie die des Assistenten:

- Requests, registriert mit `services.AddAiTool<TRequest>("name", "description")`,
- jeder Request eines geladenen Moduls, der ein `Result` liefert und `[RequiresPermission]` oder `[AiTool]` trägt; `[AiTool(Exclude = true)]` hält einen heraus,
- die Werkzeuge der Bibliothek: `list_data_sets` (die Sets, die der Benutzer lesen darf, mit Feldern und Typen), `query_data` (Zeilen eines Sets mit `filter`, `orderBy`, `select`, `top` bis 50 und `skip`, mit den Berechtigungen, Zeilenfiltern und ausgeblendeten Eigenschaften der Tabellen) und `global_search` mit dem OData-Modul, `generate_report` mit Reporting; siehe [die Werkzeuge des Assistenten](ai.md#werkzeuge).

`tools/list` nennt nur die Werkzeuge, die der Aufrufer nutzen darf: mit `Ai.Chat` und jeder Berechtigung der `[RequiresPermission]`-Attribute des Requests. Queries sind als nur lesend markiert. Jedes Command, das Daten ändert, ist als destruktiv markiert, Clients fragen ihren Benutzer, bevor sie es ausführen; ein Command, das nur hinzufügt oder gefahrlos zweimal laufen kann, nimmt sich mit `NonDestructive` aus:

```csharp
[AiTool("Pins a product to the start page.", NonDestructive = true)]
[RequiresPermission(CatalogPermissions.Products.Edit)]
public sealed record PinProductCommand(Guid Id) : ICommand<Result>;
```

Jeder Aufruf läuft als der aufrufende Benutzer durch den Dispatcher: Berechtigungen, Organisation, Validierung und Unit of Work gelten wie in der API, und Response-Filter formen das Ergebnis wie die Antworten der API. Die Antwort wird nach 20.000 Zeichen gekürzt. Jeder Aufruf landet im Audit der KI-Werkzeugaufrufe mit dem Kanal `mcp`, dem Benutzer und dem API-Schlüssel.

## Einen Client verbinden

Unter *Konto > API-Schlüssel* einen Schlüssel anlegen, mit der Berechtigung *Den Assistenten und den MCP-Server nutzen* und den Berechtigungen, die die Werkzeuge brauchen. Die Seite zeigt den Schlüssel einmal, zusammen mit der Adresse und einer fertigen Konfiguration.

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

![Konto > API-Schlüssel zeigt die MCP-Einrichtung mit dem neuen Schlüssel](../assets/screenshots/mcp-api-keys-light.png){ .shot }

## Sicherheit

- Nur JSON-Bodies erreichen die Werkzeuge, und mit einem Cookie angemeldete Anfragen von fremden Browser-Origins werden abgewiesen (`403`): Ein Formular einer fremden Seite kann keinen angemeldeten Benutzer benutzen.
- Nur Anfragen, die mit einem API-Schlüssel angemeldet sind, dürfen eine fremde `Origin` tragen (Desktop-Clients, die eine schicken); ein Schlüssel-Header allein genügt nicht. Der Server sendet keine CORS-Header, Webseiten anderer Sites können MCP-Antworten also nicht lesen.
- Ein Schlüssel handelt höchstens mit den Berechtigungen seines Benutzers und nur mit denen, die er bekommen hat; siehe [API-Schlüssel](../security/api-keys.md).
- Schlüssel gehören nicht in Repositories: Inputs von VS Code, Umgebungsvariablen oder den Secret-Speicher des Clients nutzen.
