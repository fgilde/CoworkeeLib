---
title: "KI-Assistent und MCP-Server für .NET-Anwendungen"
description: "Ein KI-Assistent in deiner ASP.NET-Core- und Blazor-App, der über Tools mit den Rechten des Benutzers arbeitet, 14 Modell-Anbieter und ein MCP-Server unter /mcp."
---

# KI-Assistent und MCP

![Assistentenfenster mit der Liste der Unterhaltungen und einer Antwort mit Tool-Chips](../assets/screenshots/mcp-ai-window-dark.png){ .shot loading=lazy }

Benutzer mit `Ai.Chat` öffnen den Assistenten über die Schaltfläche unten rechts (oder mit `Strg+I`). Er antwortet als der angemeldete Benutzer und nutzt dieselben Requests wie die Oberfläche, sieht und ändert also genau das, was diese Person sehen und ändern darf. Die Chips unter einer Antwort zeigen, welche Tools er aufgerufen hat.

## Tools sind deine vorhandenen Requests

Eine eigene Tool-Schicht, die du pflegen müsstest, gibt es nicht. Jeder Request eines Moduls, der ein `Result` liefert und `[RequiresPermission]` oder `[AiTool]` trägt, wird zum Tool. Die `[Description]` erklärt dem Modell, was er tut, der Request-Typ ist das Eingabeschema:

```csharp
[RequiresPermission(CatalogPermissions.Brands.View)]
[Description("Lists brands, optionally filtered by name.")]
public sealed record GetBrandsQuery(string? Search) : IQuery<Result<IReadOnlyList<BrandDto>>>;
```

Mit `[AiTool(Exclude = true)]` bleibt ein Request draußen; alles, was Berechtigungen ändert, ist ein naheliegender Kandidat. Mit dem OData-Modul kommen `list_data_sets` und `query_data` dazu, die jede registrierte Entitätsmenge lesen, mit denselben Zeilenfiltern wie die Tabellen. In der Demo beantwortet der Assistent damit Fragen zu den 100.000 Aufträgen, ohne dass jemand ein Tool dafür geschrieben hat.

Abfragen laufen sofort. Commands warten:

![Der Assistent wartet auf Bestätigung, bevor er einen destruktiven Command ausführt](../assets/screenshots/mcp-ai-confirm-light.png){ .shot loading=lazy }

Das Fenster zeigt, was der Assistent ausführen will, Geheimnisse maskiert, und erst dein Klick auf *Ausführen* startet es. Text, den jemand in einen Datensatz oder in den Chat geschmuggelt hat, kann deshalb nichts von allein ändern. Ein wartender Aufruf liegt verschlüsselt auf dem Server und läuft höchstens einmal, auch wenn zwei Tabs gleichzeitig klicken.

Antworten kommen als Markdown, dem die scharfen Kanten fehlen: rohes HTML erscheint als Text, Bilder werden zu Links, und externe Links verlieren ihre Query, damit sie keine Daten hinaustragen.

## 14 Anbieter, zur Laufzeit umschaltbar

Das Modell steckt hinter `IChatClient` aus `Microsoft.Extensions.AI`. Host-Admins wählen es unter **Administration > System > KI**, schon die nächste Nachricht nutzt es, ohne Neustart.

| Anbieter | Id | | Anbieter | Id |
|---|---|---|---|---|
| Anthropic (Standard) | `anthropic` | | Groq | `groq` |
| OpenAI | `openAI` | | DeepSeek | `deepSeek` |
| Azure OpenAI | `azureOpenAI` | | xAI Grok | `xai` |
| Google Gemini | `gemini` | | Together AI | `together` |
| OpenRouter | `openRouter` | | Fireworks AI | `fireworks` |
| Mistral | `mistral` | | Ollama (lokal) | `ollama` |
| Eigener, OpenAI-kompatibel | `openAICompatible` | | LM Studio (lokal) | `lmStudio` |

Der Schlüssel wird verschlüsselt gespeichert und nie an den Browser zurückgegeben. *Verbindung testen* schickt einen kurzen Prompt und eine Probe für Tool-Aufrufe. Mach das, bevor du am Framework zweifelst: Kleine lokale Modelle rufen Tools weniger zuverlässig auf. Eigene Funktionen, etwa ein Button für Zusammenfassungen, holen sich mit `AiConnections.CreateClientAsync` einen Client auf derselben Verbindung.

## Die App als MCP-Server

Jede App stellt ihre Tools außerdem unter `/mcp` per Streamable HTTP bereit. Claude Desktop, Claude Code, VS Code oder Cursor verbinden sich mit einem persönlichen API-Schlüssel und handeln als dessen Besitzer; ein KI-Anbieter ist dabei nicht im Spiel.

```bash
claude mcp add --transport http myapp https://myapp.example/mcp --header "Authorization: Bearer cwk_..."
```

Schlüssel legst du unter *Konto > API-Schlüssel* an, mit einer Laufzeit von 7 bis 365 Tagen und einem Teil deiner eigenen Rechte. Der Server speichert nur einen SHA-256-Hash und prüft bei jeder Anfrage die aktuellen Rechte des Benutzers. `tools/list` zeigt nur, was der Aufrufer nutzen darf, Commands sind als destruktiv markiert, damit der Client vorher nachfragt, und jeder Aufruf landet im Audit der KI-Tool-Aufrufe.

Die CLI hat mit `coworkee mcp` einen eigenen MCP-Server für den Schritt davor: Ein Agent auf deinem Rechner schreibt und prüft die Spezifikation einer App, legt sie an und ergänzt Module und Entities mit denselben Befehlen, die du tippen würdest.

**Stand:** Bei mehreren API-Instanzen brauchen wartende Assistenten-Aufrufe einen gemeinsamen Data-Protection-Schlüsselring, und die Sperre, die zwei gleichzeitige Antworten für denselben Benutzer verhindert, gilt nur pro Instanz.

Referenz: [KI-Assistent und MCP](../modules/ai.md), [MCP-Server der App](../modules/mcp.md), [API-Schlüssel](../security/api-keys.md), [MCP-Server der CLI](../getting-started/mcp.md).
