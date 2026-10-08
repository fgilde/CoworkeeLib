# AI assistant and MCP

`Coworkee.Ai` lets Claude work with the app through the same requests the UI uses. Every tool is a dispatcher request, so permissions, validation and audit apply to the assistant exactly as to a person.

```csharp
services.AddAiTool<SearchAssetsQuery>("search_assets", "Finds assets by text, folder and tags.");
services.AddAiTool<TagAssetsCommand>("tag_assets", "Adds tags to assets.");
```

Without any registration, every request of a module that returns a `Result` and carries `[RequiresPermission]` or `[AiTool]` becomes a tool. Its `[Description]` is the tool description; `[AiTool(Exclude = true)]` keeps a request out, for example one that changes permissions:

```csharp
[RequiresPermission(CatalogPermissions.Brands.View)]
[Description("Lists brands, optionally filtered by name.")]
public sealed record GetBrandsQuery(string? Search) : IQuery<Result<IReadOnlyList<BrandDto>>>;
```

The request type becomes the tool's input schema, the `Result` its output. Failures reach the model as messages it can react to.

- **Assistant page** for users with `Ai.Chat`: a chat with the tools of all modules, as the signed-in user.
- **MCP server** under `/mcp` with the same tools, for Claude Desktop, Claude Code and other MCP clients; it requires a signed-in user.
- **Tool call audit** for users with `Ai.Audit`: every call with input, result and user.
- Commands, which change data, only run in the first round of a turn. A tool result can therefore not trick the model into changing something on its own.

| Setting | |
|---|---|
| `Ai.Enabled` | on or off |
| `Ai.ApiKey` | Anthropic API key (secret) |
| `Ai.Model` | default `claude-opus-5-5` |
| `Ai.MaxToolRounds` | how many tool rounds one answer may take |
