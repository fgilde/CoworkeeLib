---
title: "AI assistant and MCP server for .NET business apps"
description: "An AI assistant inside your ASP.NET Core and Blazor app that works through tools with the user's permissions, 14 model providers and an MCP server at /mcp."
---

# An AI assistant and MCP server for your .NET application

An assistant in a business app is only useful when it can do things: find the open orders of a customer, tag a batch of files, draft a product. It is only safe when it can do exactly what the person in front of it may do, and nothing happens behind their back.

Coworkee builds the assistant on the requests your modules already have. Every tool is a dispatcher request, so permissions, tenant filters, validation and audit apply to the model exactly as to a person clicking through the UI. The same tools are offered to external agents over MCP, and a separate MCP server in the CLI helps you build the app in the first place.

![Assistant window with the list of conversations and an answer with tool chips](../assets/screenshots/mcp-ai-window-dark.png){ .shot loading=lazy }

## Tools from the requests you already wrote

Without any registration, every request of a module that returns a `Result` and carries `[RequiresPermission]` or `[AiTool]` becomes a tool. Its `[Description]` tells the model what it does, and the request type becomes the input schema:

```csharp
[RequiresPermission(CatalogPermissions.Brands.View)]
[Description("Lists brands, optionally filtered by name.")]
public sealed record GetBrandsQuery(string? Search) : IQuery<Result<IReadOnlyList<BrandDto>>>;
```

`[AiTool(Exclude = true)]` keeps a request out, for example one that changes permissions. Requests from elsewhere register with `services.AddAiTool<TRequest>("name", "description")`. With the OData module, the assistant also gets `list_data_sets` and `query_data`, which read any registered entity set with the same row filters and hidden properties as the tables.

## The assistant window

Users with `Ai.Chat` open the window from a button at the bottom right or with `Ctrl+I`. It comes small, large or full screen, and full screen on phones. Answers stream in as Markdown that cannot carry markup: raw HTML shows as text, images become links, and links to other sites lose their query string, so a planted link cannot carry data away. Conversations are stored per user on the server, searchable, live in every open tab, and part of the personal data export.

Tool chips under each answer show what the assistant did. With `AssistantLinks` in the client options they link to the record it created or changed.

## Writes need a click

Queries run at once. Every command, anything that changes data, waits: the window shows exactly what the assistant wants to run, with inputs that look like secrets masked, and only the user's *Run* button executes it. Destructive commands are highlighted. Text planted in a record or in the conversation can therefore never change anything on its own. The waiting call is stored encrypted with ASP.NET Data Protection, decided once even when two tabs click at the same time, and never runs twice.

![The assistant waits for confirmation before running a destructive command](../assets/screenshots/mcp-ai-confirm-light.png){ .shot loading=lazy }

## 14 AI providers, chosen by an administrator

The assistant talks to the model through `IChatClient` of `Microsoft.Extensions.AI`, with the same tools, rules and audit for every provider:

| Provider | Id | Notes |
|---|---|---|
| Anthropic | `anthropic` | the default |
| OpenAI | `openAI` | GPT models |
| Azure OpenAI | `azureOpenAI` | data stays in your Azure tenant |
| Google Gemini | `gemini` | free tier |
| OpenRouter | `openRouter` | many models behind one key |
| Mistral | `mistral` | hosted in the EU |
| Groq | `groq` | open models at high speed |
| DeepSeek | `deepSeek` | low prices |
| xAI Grok | `xai` | Grok models |
| Together AI | `together` | open models in the cloud |
| Fireworks AI | `fireworks` | open models in the cloud |
| Ollama | `ollama` | local and offline |
| LM Studio | `lmStudio` | local |
| Custom | `openAICompatible` | any server that speaks the OpenAI chat API |

Host administrators set it up under **Administration > System > AI**: pick a provider card, enter address, key and model, and save. Changes apply to the next message, without a restart. The key is stored encrypted and never sent back to the browser. *Load models* asks the provider for its model list, and *Test connection* sends a short prompt plus a tool call probe and shows latency, answer and whether the model calls tools at all.

For features of your own, such as summaries or translations, `AiConnections.CreateClientAsync` hands you a chat client on the configured connection.

## The app's own MCP server at /mcp

Every Coworkee app is an MCP server. Claude Desktop, Claude Code, VS Code, Cursor and other MCP clients connect to `https://myapp.example/mcp` over Streamable HTTP and use the app's tools as the user who owns the API key. No model or AI provider is needed for that.

Users create a personal key under *Account > API keys*: a name, an expiry of 7 to 365 days and the permissions it may use, picked from their own. The key is shown once; the server keeps only its SHA-256 hash. Its effective permissions are re-checked against the user's current permissions on every request.

```bash
claude mcp add --transport http myapp https://myapp.example/mcp --header "Authorization: Bearer cwk_..."
```

`tools/list` names only the tools the caller may use. Queries are marked read only and commands destructive, so clients ask before running them; a command that only adds can opt out with `[AiTool(NonDestructive = true)]`. Every call lands in the AI tool call audit with channel, user and key.

## The CLI's MCP server for building apps

`coworkee mcp` is a different server: it runs on your machine over stdio and lets an agent plan an app with you, write and validate its spec, create it and add modules, entities and migrations with the same commands you would type. The Studio offers the same choices as a wizard, including the AI provider of the new app.

![The AI assistant step of the Studio with the 14 provider cards](../assets/screenshots/studio-ai-light.webp){ .shot loading=lazy }

```bash
claude mcp add coworkee -- coworkee mcp
```

## Where to go next

- [AI assistant and MCP](../modules/ai.md): tools, the assistant window, providers, settings and code
- [MCP server of the app](../modules/mcp.md): transport, client configurations and security
- [API keys](../security/api-keys.md): creating keys, effective permissions and audit
- [MCP server of the CLI](../getting-started/mcp.md): registering it and its tools
- [Permissions](../security/permissions.md): the checks every tool call goes through
