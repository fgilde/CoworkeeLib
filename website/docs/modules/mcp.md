# MCP server of the app

Every Coworkee app is an [MCP](https://modelcontextprotocol.io) server: Claude Desktop, Claude Code, VS Code, Cursor and other MCP clients connect to it and use the app's tools as the user who owns the API key. The tools are the same as those of the [AI assistant](ai.md); no model or AI provider is needed for MCP.

- **Transport**: Streamable HTTP, stateless, so every request is authenticated on its own.
- **Address**: the API maps it under `/mcp`. The web host forwards `/mcp` to the API as well, so clients use the address of the app (`https://myapp.example/mcp`). The browser session never goes along: over the web host, only API keys reach the tools.
- **Who**: users with the permission `Ai.Chat`. With an [API key](../security/api-keys.md), `Ai.Chat` must be among the key's permissions.
- **Rate limit**: the `ai` policy, counted per user.

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

`Enabled` is on by default; `false` maps no MCP endpoint at all.

## Tools

The tools come from the same places as the assistant's:

- requests registered with `services.AddAiTool<TRequest>("name", "description")`,
- every request of a loaded module that returns a `Result` and carries `[RequiresPermission]` or `[AiTool]`; `[AiTool(Exclude = true)]` keeps one out,
- with the OData module, `list_data_sets` (the sets the user may read, with their fields and types) and `query_data` (rows of a set with `filter`, `orderBy`, `select`, `top` and `skip`, with the permissions, row filters and hidden properties of the tables).

`tools/list` names only the tools the caller may use. Queries are marked read only. Commands named `delete_…`, `remove_…`, `revoke_…`, `purge_…` or `erase_…`, and those marked `[AiTool(Destructive = true)]`, are marked destructive, so clients ask their user before they run them.

```csharp
[AiTool("Deletes a product.", Destructive = true)]
[RequiresPermission(CatalogPermissions.Products.Delete)]
public sealed record DropProductCommand(Guid Id) : ICommand<Result>;
```

Every call runs through the dispatcher as the calling user: permissions, organisation, validation and the unit of work apply as in the API, and response filters shape the result as they shape the API's answers. Each call lands in the AI tool call audit with the channel `mcp`, the user and the API key.

## Connecting a client

Create a key under *Account > API keys*, with the permission *Use the assistant and the MCP server* and the permissions the tools need. The page shows the key once, together with the address and a ready configuration.

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

Versions without remote servers in the configuration file go through `mcp-remote`:

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

`.vscode/mcp.json` – VS Code asks for the key once and stores it:

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

Transport *Streamable HTTP*, URL `https://myapp.example/mcp`, header `Authorization` with `Bearer cwk_...`.

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

## Security

- Only JSON bodies reach the tools, and requests signed in with a cookie from a foreign browser origin are refused: a cross site form cannot use a signed-in user.
- An API key is no ambient credential a browser sends on its own, so requests with a key may come from any origin (browser based MCP clients).
- A key acts with at most the permissions of its user, and only with those it was given; see [API keys](../security/api-keys.md).
- Keep keys out of repositories: use the inputs of VS Code, environment variables or the secret store of the client.
