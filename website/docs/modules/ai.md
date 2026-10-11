# AI assistant and MCP

`Coworkee.Ai` lets a language model (Claude, GPT, a local model through Ollama, ...) work with the app through the same requests the UI uses. Every tool is a dispatcher request, so permissions, validation and audit apply to the assistant exactly as to a person.

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

With the OData module, the assistant also gets a `query_data` tool: it lists rows of any registered entity set with an OData filter and order, with the same permissions, row filters and hidden properties as the tables.

The request type becomes the tool's input schema, the `Result` its output. Failures reach the model as messages it can react to.

- **Assistant page** for users with `Ai.Chat`: a chat with the tools of all modules, as the signed-in user.
- **MCP server** under `/mcp` with the same tools, for Claude Desktop, Claude Code and other MCP clients; it requires a signed-in user.
- **Tool call audit** for users with `Ai.Audit`: every call with input, result and user.
- Commands, which change data, only run in the first round of a turn. A tool result can therefore not trick the model into changing something on its own.

## Providers

The assistant talks to the model through `IChatClient` of `Microsoft.Extensions.AI`, with the same tools, rules and audit for every provider. The settings are global only (host administrators), the key is a secret setting: it is stored encrypted and never sent to the browser.

| Setting | |
|---|---|
| `Ai.Enabled` | on or off |
| `Ai.Provider` | `anthropic` (default), `openAI`, `azureOpenAI`, `ollama`, `openAICompatible` |
| `Ai.Model` | empty: the provider's default (`claude-opus-5-5`, `gpt-5`, `llama3.2`); Azure OpenAI: the deployment name |
| `Ai.Endpoint` | Azure OpenAI resource (`https://acme.openai.azure.com`), Ollama address (default `http://localhost:11434`) or the base address of an OpenAI compatible server (`http://localhost:1234/v1`) |
| `Ai.ApiKey` | API key (secret); Ollama and OpenAI compatible servers may go without |
| `Ai.MaxToolRounds` | how many tool rounds one answer may take |

| Provider | Needs | Good for |
|---|---|---|
| `anthropic` | key | the most reliable tool use |
| `openAI` | key | GPT models |
| `azureOpenAI` | endpoint, deployment, key | data that has to stay in your Azure tenant; uses the v1 API of Azure OpenAI |
| `ollama` | nothing | trying it locally and offline; small models call tools less reliably |
| `openAICompatible` | endpoint, model | gateways, vLLM, LM Studio, LiteLLM and other servers that speak the OpenAI chat API |

Like every setting they have defaults in the configuration, e.g. from the app host or environment variables:

```json
{
  "Coworkee": {
    "Settings": {
      "Defaults": {
        "Ai.Enabled": "true",
        "Ai.Provider": "azureOpenAI",
        "Ai.Endpoint": "https://acme.openai.azure.com",
        "Ai.Model": "gpt-5-assistant"
      }
    }
  }
}
```

`coworkee new --ai-provider ollama` creates an app host with an Ollama container that pulls `llama3.2` and switches the assistant on. For the other providers, the app host takes key, endpoint and model from its user secrets, if there:

```bash
dotnet user-secrets set Parameters:ai-apikey sk-... --project src/Shop.AppHost
dotnet user-secrets set Parameters:ai-endpoint https://acme.openai.azure.com --project src/Shop.AppHost
dotnet user-secrets set Parameters:ai-model gpt-5-assistant --project src/Shop.AppHost
```

Another model service (Gemini, Bedrock, a company gateway) plugs in through `IAiChatClientFactory`:

```csharp
services.AddSingleton<IAiChatClientFactory, GeminiChatClients>();

internal sealed class GeminiChatClients : IAiChatClientFactory
{
    public IChatClient Create(AiConnection connection) => new GeminiChatClient(connection.ApiKey!, connection.EffectiveModel ?? "gemini-2.5-pro");
}
```

The generated service defaults take the `Coworkee.Ai` HTTP client out of the standard resilience handler: answers often take longer than its timeouts, and the provider clients retry on their own.
