# MCP-Server der CLI

`coworkee mcp` startet einen [Model-Context-Protocol](https://modelcontextprotocol.io)-Server über stdio. Ein KI-Agent wie Claude Code oder Claude Desktop kann dann mit Ihnen eine App planen, ihre Spezifikation schreiben, sie anlegen und Module und Entities ergänzen, mit denselben Befehlen, die Sie tippen würden.

Das ist der MCP-Server der Kommandozeile auf Ihrem Rechner. Eine erzeugte App hat einen eigenen MCP-Endpunkt unter `/mcp` mit den Tools der App, siehe [KI-Assistent und MCP](../modules/ai.md).

## Registrieren

Die CLI muss als globales Tool installiert sein (`dotnet tool install -g Coworkee.Cli`).

=== "Claude Code"

    ```bash
    claude mcp add coworkee -- coworkee mcp
    ```

=== "Claude Desktop"

    ```json title="claude_desktop_config.json"
    {
      "mcpServers": {
        "coworkee": { "command": "coworkee", "args": ["mcp"] }
      }
    }
    ```

=== "VS Code"

    ```json title=".vscode/mcp.json"
    {
      "servers": {
        "coworkee": { "type": "stdio", "command": "coworkee", "args": ["mcp"] }
      }
    }
    ```

Andere Clients nehmen denselben Befehl: `coworkee` mit dem Argument `mcp`. `coworkee mcp --help` zeigt diese Schnipsel.

## Tools

| Tool | Was es tut |
|---|---|
| `list_options` | Die [Optionen](cli.md#optionen) mit Id, Art, Standard, Auswahl, Vor- und Nachteilen und wann man sie wählt; auf Wunsch eine Gruppe |
| `get_spec_schema` | JSON Schema einer [App-Spezifikation](cli.md#app-spezifikationen) |
| `get_spec_example` | Eine vollständige Beispiel-Spezifikation |
| `validate_spec` | Prüft eine Spezifikation, mit Ordner auch ihre Logo- und Mediendateien |
| `create_app` | `coworkee new` aus einer Spezifikation oder aus Name und Optionen, in einem Ordner |
| `add_module` | `coworkee module add` mit Entity, Eigenschaften, Client-Entity und Mapping |
| `add_entity` | `coworkee entity add` |
| `add_migration` | `coworkee migrations add` |
| `run_doctor` | `coworkee doctor` |
| `update_packages` | `coworkee update` |
| `search_docs` | Durchsucht diese Dokumentation (den veröffentlichten Suchindex; offline die Seitentitel) |

Die Tools, die etwas ändern, starten die CLI als Kindprozess im angegebenen Ordner und antworten mit Exit-Code und Ausgabe; sie verhalten sich also genau wie die Befehle. Eine App anzulegen dauert Minuten: Template, Restore und, wenn nötig, die erste Migration.

## Eine Sitzung

> Leg in C:\work eine Shop-App an: ohne Beispiele, ohne Chat, mit Dateien und dem Theme Forest in unserem Grün #0a7d5a. Sie braucht Kunden (Name, E-Mail) und Bestellungen (Nummer, Summe, Bestelldatum), die Benutzer den ganzen Tag filtern.

Der Agent liest `list_options` und `get_spec_schema`, schreibt eine Spezifikation mit einem Modul `Sales`, markiert `Order` als Client-Entity, prüft sie mit `validate_spec` und ruft `create_app` auf. Danach erweitern `add_entity` und `add_migration` die App auf dieselbe Weise.

Der Server stellt sich dem Agenten mit dieser Reihenfolge vor, eine schlichte Bitte genügt also.
