---
title: "KI-Assistent und MCP-Server für .NET-Anwendungen"
description: "Ein KI-Assistent in Ihrer ASP.NET-Core- und Blazor-App, der über Tools mit den Rechten des Benutzers arbeitet, 14 Modell-Anbieter und ein MCP-Server unter /mcp."
---

# KI-Assistent und MCP-Server für Ihre .NET-Anwendung

Ein Assistent in einer Geschäftsanwendung nützt nur, wenn er etwas tun kann: die offenen Aufträge eines Kunden finden, einen Stapel Dateien verschlagworten, ein Produkt anlegen. Sicher ist er nur, wenn er genau das darf, was die Person davor darf, und nichts hinter ihrem Rücken passiert.

Coworkee baut den Assistenten auf den Requests auf, die Ihre Module ohnehin haben. Jedes Tool ist ein Dispatcher-Request, Berechtigungen, Mandantenfilter, Validierung und Audit gelten für das Modell also genauso wie für einen Menschen, der sich durch die Oberfläche klickt. Dieselben Tools stehen externen Agenten über MCP zur Verfügung, und ein eigener MCP-Server in der CLI hilft Ihnen, die App überhaupt erst zu bauen.

![Assistentenfenster mit der Liste der Unterhaltungen und einer Antwort mit Tool-Chips](../assets/screenshots/mcp-ai-window-dark.png){ .shot loading=lazy }

## Tools aus den Requests, die Sie schon geschrieben haben

Ohne jede Registrierung wird jeder Request eines Moduls, der ein `Result` liefert und `[RequiresPermission]` oder `[AiTool]` trägt, zum Tool. Seine `[Description]` sagt dem Modell, was er tut, der Request-Typ wird zum Eingabeschema:

```csharp
[RequiresPermission(CatalogPermissions.Brands.View)]
[Description("Lists brands, optionally filtered by name.")]
public sealed record GetBrandsQuery(string? Search) : IQuery<Result<IReadOnlyList<BrandDto>>>;
```

`[AiTool(Exclude = true)]` hält einen Request heraus, etwa einen, der Berechtigungen ändert. Requests von anderswo registrieren Sie mit `services.AddAiTool<TRequest>("name", "description")`. Mit dem OData-Modul bekommt der Assistent außerdem `list_data_sets` und `query_data`, die jede registrierte Entitätsmenge lesen, mit denselben Zeilenfiltern und verborgenen Eigenschaften wie die Tabellen.

## Das Assistentenfenster

Benutzer mit `Ai.Chat` öffnen das Fenster über eine Schaltfläche unten rechts oder mit `Strg+I`. Es gibt es klein, groß und im Vollbild, auf Telefonen immer im Vollbild. Antworten erscheinen während des Schreibens als Markdown, das kein Markup tragen kann: rohes HTML erscheint als Text, Bilder werden zu Links, und Links auf fremde Seiten verlieren ihre Query, damit ein eingeschleuster Link keine Daten hinausträgt. Unterhaltungen liegen je Benutzer auf dem Server, sind durchsuchbar, live in jedem offenen Tab und Teil des Exports personenbezogener Daten.

Tool-Chips unter jeder Antwort zeigen, was der Assistent getan hat. Mit `AssistantLinks` in den Client-Optionen verlinken sie auf den Datensatz, den er angelegt oder geändert hat.

## Schreiben erst nach einem Klick

Abfragen laufen sofort. Jeder Command, also alles, was Daten ändert, wartet: Das Fenster zeigt genau, was der Assistent ausführen möchte, Eingaben, die wie Geheimnisse aussehen, maskiert, und erst der Klick auf *Ausführen* führt es aus. Destruktive Commands sind hervorgehoben. Text, der in einem Datensatz oder in der Unterhaltung eingeschleust wurde, kann deshalb nie von sich aus etwas ändern. Der wartende Aufruf wird mit ASP.NET Data Protection verschlüsselt gespeichert, nur einmal entschieden, auch wenn zwei Tabs gleichzeitig klicken, und läuft nie zweimal.

![Der Assistent wartet auf Bestätigung, bevor er einen destruktiven Command ausführt](../assets/screenshots/mcp-ai-confirm-light.png){ .shot loading=lazy }

## 14 KI-Anbieter, gewählt vom Administrator

Der Assistent spricht mit dem Modell über `IChatClient` aus `Microsoft.Extensions.AI`, mit denselben Tools, Regeln und demselben Audit für jeden Anbieter:

| Anbieter | Id | Hinweis |
|---|---|---|
| Anthropic | `anthropic` | die Voreinstellung |
| OpenAI | `openAI` | GPT-Modelle |
| Azure OpenAI | `azureOpenAI` | Daten bleiben in Ihrem Azure-Tenant |
| Google Gemini | `gemini` | kostenloses Kontingent |
| OpenRouter | `openRouter` | viele Modelle hinter einem Schlüssel |
| Mistral | `mistral` | in der EU gehostet |
| Groq | `groq` | offene Modelle mit hoher Geschwindigkeit |
| DeepSeek | `deepSeek` | niedrige Preise |
| xAI Grok | `xai` | Grok-Modelle |
| Together AI | `together` | offene Modelle in der Cloud |
| Fireworks AI | `fireworks` | offene Modelle in der Cloud |
| Ollama | `ollama` | lokal und offline |
| LM Studio | `lmStudio` | lokal |
| Eigener | `openAICompatible` | jeder Server, der die Chat-API von OpenAI spricht |

Host-Administratoren richten ihn unter **Administration > System > KI** ein: Anbieterkarte wählen, Adresse, Schlüssel und Modell eintragen, speichern. Änderungen gelten ab der nächsten Nachricht, ohne Neustart. Der Schlüssel wird verschlüsselt gespeichert und nie an den Browser zurückgeschickt. *Modelle laden* fragt den Anbieter nach seiner Modellliste, *Verbindung testen* schickt einen kurzen Prompt und eine Probe für Tool-Aufrufe und zeigt Latenz, Antwort und ob das Modell überhaupt Tools aufruft.

Für eigene Funktionen wie Zusammenfassungen oder Übersetzungen liefert `AiConnections.CreateClientAsync` einen Chat-Client auf der eingestellten Verbindung.

## Der eigene MCP-Server der App unter /mcp

Jede Coworkee-App ist ein MCP-Server. Claude Desktop, Claude Code, VS Code, Cursor und andere MCP-Clients verbinden sich per Streamable HTTP mit `https://myapp.example/mcp` und nutzen die Tools der App als der Benutzer, dem der API-Schlüssel gehört. Ein Modell oder KI-Anbieter ist dafür nicht nötig.

Benutzer legen unter *Konto > API-Schlüssel* einen persönlichen Schlüssel an: Name, Ablauf nach 7 bis 365 Tagen und die Rechte, die er nutzen darf, ausgewählt aus den eigenen. Der Schlüssel wird einmal angezeigt, der Server behält nur seinen SHA-256-Hash. Seine wirksamen Rechte werden bei jeder Anfrage gegen die aktuellen Rechte des Benutzers geprüft.

```bash
claude mcp add --transport http myapp https://myapp.example/mcp --header "Authorization: Bearer cwk_..."
```

`tools/list` nennt nur die Tools, die der Aufrufer nutzen darf. Abfragen sind als nur lesend markiert, Commands als destruktiv, Clients fragen also vor dem Ausführen nach; ein Command, der nur hinzufügt, nimmt sich mit `[AiTool(NonDestructive = true)]` davon aus. Jeder Aufruf landet im Audit der KI-Tool-Aufrufe, mit Kanal, Benutzer und Schlüssel.

## Der MCP-Server der CLI zum Bauen von Apps

`coworkee mcp` ist ein anderer Server: Er läuft auf Ihrem Rechner über stdio und lässt einen Agenten mit Ihnen eine App planen, ihre Spezifikation schreiben und prüfen, sie anlegen und Module, Entities und Migrationen hinzufügen, mit denselben Befehlen, die Sie tippen würden. Das Studio bietet dieselben Entscheidungen als Assistenten an, einschließlich des KI-Anbieters der neuen App.

![Der Schritt KI-Assistent im Studio mit den 14 Anbieterkarten](../assets/screenshots/studio-ai-light-de.webp){ .shot loading=lazy }

```bash
claude mcp add coworkee -- coworkee mcp
```

## Weiterlesen

- [KI-Assistent und MCP](../modules/ai.md): Tools, Assistentenfenster, Anbieter, Einstellungen und Code
- [MCP-Server der App](../modules/mcp.md): Transport, Client-Konfigurationen und Sicherheit
- [API-Schlüssel](../security/api-keys.md): Schlüssel anlegen, wirksame Rechte und Audit
- [MCP-Server der CLI](../getting-started/mcp.md): Registrierung und Tools
- [Berechtigungen](../security/permissions.md): die Prüfungen, die jeder Tool-Aufruf durchläuft
