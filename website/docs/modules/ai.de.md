# KI-Assistent und MCP

`Coworkee.Ai` lässt ein Sprachmodell (Claude, GPT, ein lokales Modell über Ollama, ...) mit der App über dieselben Requests arbeiten, die auch die Oberfläche nutzt. Jedes Werkzeug ist ein Dispatcher-Request; Berechtigungen, Validierung und Audit gelten für den Assistenten genau wie für einen Menschen.

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

Mit dem OData-Modul bekommt der Assistent zusätzlich ein Werkzeug `query_data`: es listet Zeilen jedes registrierten Entity-Sets mit OData-Filter und Sortierung, mit denselben Berechtigungen, Zeilenfiltern und ausgeblendeten Eigenschaften wie die Tabellen.

Der Request-Typ wird zum Eingabeschema des Werkzeugs, das `Result` zu seiner Ausgabe. Fehler erreichen das Modell als Meldungen, auf die es reagieren kann.

- **Assistent-Seite** für Benutzer mit `Ai.Chat`: ein Chat mit den Werkzeugen aller Module, als angemeldeter Benutzer.
- **MCP-Server** unter `/mcp` mit denselben Werkzeugen, für Claude Desktop, Claude Code und andere MCP-Clients; er verlangt einen angemeldeten Benutzer.
- **Werkzeug-Audit** für Benutzer mit `Ai.Audit`: jeder Aufruf mit Eingabe, Ergebnis und Benutzer.
- Commands, die Daten ändern, laufen nur in der ersten Runde einer Antwort. Ein Werkzeugergebnis kann das Modell so nicht dazu bringen, eigenmächtig etwas zu ändern.

## Anbieter

Der Assistent spricht über `IChatClient` aus `Microsoft.Extensions.AI` mit dem Modell, mit denselben Werkzeugen, Regeln und demselben Audit für jeden Anbieter. Die Einstellungen sind nur global (Host-Administratoren), der Schlüssel ist eine geheime Einstellung: Er wird verschlüsselt gespeichert und nie an den Browser geschickt.

| Einstellung | |
|---|---|
| `Ai.Enabled` | an oder aus |
| `Ai.Provider` | `anthropic` (Standard), `openAI`, `azureOpenAI`, `ollama`, `openAICompatible` |
| `Ai.Model` | leer: der Standard des Anbieters (`claude-opus-5-5`, `gpt-5`, `llama3.2`); Azure OpenAI: der Deployment-Name |
| `Ai.Endpoint` | Azure-OpenAI-Ressource (`https://acme.openai.azure.com`), Ollama-Adresse (Standard `http://localhost:11434`) oder die Basisadresse eines OpenAI-kompatiblen Servers (`http://localhost:1234/v1`) |
| `Ai.ApiKey` | API-Schlüssel (geheim); Ollama und OpenAI-kompatible Server kommen ohne aus |
| `Ai.MaxToolRounds` | wie viele Werkzeugrunden eine Antwort dauern darf |

| Anbieter | Braucht | Gut für |
|---|---|---|
| `anthropic` | Schlüssel | die zuverlässigste Werkzeugnutzung |
| `openAI` | Schlüssel | GPT-Modelle |
| `azureOpenAI` | Endpunkt, Deployment, Schlüssel | Daten, die in Ihrem Azure-Mandanten bleiben müssen; nutzt die v1-API von Azure OpenAI |
| `ollama` | nichts | lokales Ausprobieren, auch offline; kleine Modelle rufen Werkzeuge weniger zuverlässig auf |
| `openAICompatible` | Endpunkt, Modell | Gateways, vLLM, LM Studio, LiteLLM und andere Server, die die OpenAI-Chat-API sprechen |

Wie jede Einstellung haben sie Standardwerte in der Konfiguration, etwa aus dem AppHost oder aus Umgebungsvariablen:

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

`coworkee new --ai-provider ollama` erzeugt einen AppHost mit einem Ollama-Container, der `llama3.2` lädt, und schaltet den Assistenten ein. Für die anderen Anbieter nimmt der AppHost Schlüssel, Endpunkt und Modell aus seinen User-Secrets, sofern vorhanden:

```bash
dotnet user-secrets set Parameters:ai-apikey sk-... --project src/Shop.AppHost
dotnet user-secrets set Parameters:ai-endpoint https://acme.openai.azure.com --project src/Shop.AppHost
dotnet user-secrets set Parameters:ai-model gpt-5-assistant --project src/Shop.AppHost
```

Ein anderer Modelldienst (Gemini, Bedrock, ein Firmen-Gateway) kommt über `IAiChatClientFactory` dazu:

```csharp
services.AddSingleton<IAiChatClientFactory, GeminiChatClients>();

internal sealed class GeminiChatClients : IAiChatClientFactory
{
    public IChatClient Create(AiConnection connection) => new GeminiChatClient(connection.ApiKey!, connection.EffectiveModel ?? "gemini-2.5-pro");
}
```

Die erzeugten Service-Defaults nehmen den HTTP-Client `Coworkee.Ai` aus dem Standard-Resilience-Handler: Antworten dauern oft länger als dessen Timeouts, und die Anbieter-Clients wiederholen selbst.
