---
title: ".NET-Apps mit KI-Agenten auf Coworkee bauen"
description: "Warum Coworkee eine gute Basis für Apps ist, die du mit Claude Code, Copilot oder Cursor schreibst: Sicherheit, Mandanten, Audit, Realtime und die Admin-Oberfläche sind Pakete, der Agent schreibt nur die Fachlichkeit."
---

# Mit KI bauen

Ein Coding-Agent ist stark bei Fachcode: eine Entity, ein Validator, eine Seite, ein Bericht. Weniger verlässlich ist er bei den Teilen, die überall gleichzeitig stimmen müssen: eine Berechtigung bei jedem Request prüfen, die Zeilen eines Mandanten von denen eines anderen trennen, das Audit in derselben Transaktion schreiben, nie ein Access-Token in den Browser schicken. Genau dort hinterlässt ein Prompt, der eine Zeile vergisst, eine Lücke.

In einer Coworkee-App ist das nicht die Aufgabe des Agenten. Diese Teile kommen mit den Paketen, die Tests der Bibliothek decken sie ab, und der Code des Agenten steckt sich hinein. Der Agent schreibt die Fachlichkeit, der Rest ist schon da.

## Was der Agent nicht bauen muss

| Thema | Woher es kommt |
|---|---|
| Anmeldung, Benutzer, Rollen, Gruppen, externe Anbieter | [Identity](identity.md): ein OpenIddict-Auth-Server mit Keycloak oder einem beliebigen OpenID-Connect-Anbieter; der Browser sieht nie ein Access-Token, und Auth-Server und Web-App schicken eine Content-Security-Policy |
| Berechtigungen | ein `[RequiresPermission]` am Request; die [Pipeline](../fundamentals/messaging.md) prüft es für Endpunkte, Jobs, den Assistenten und MCP gleichermaßen |
| Mandanten | `IMultiTenant` an der Entity; der DbContext filtert jede Abfrage und setzt den Mandanten beim Einfügen ([Mandanten](multi-tenancy.md)) |
| Validierung und Transaktionen | FluentValidation und die Unit of Work laufen um jeden Request; ein Handler ruft kein `SaveChangesAsync` |
| Audit, Verlauf und Rückgängig | [Audit](../modules/auditing.md) auf Feldebene in derselben Transaktion, Verlauf eines Datensatzes mit Wiederherstellen und Rückgängig pro Feld |
| Realtime | `[Realtime]` an der Entity, und offene Tabellen und Detailseiten laden neu, wenn jemand anderes sie ändert ([Realtime](realtime.md)) |
| Workflows und Genehmigungen | [Zustände, Übergänge, Genehmigungen](../modules/workflows.md) mit Fristen, Erinnerungen und Eskalation |
| Suche, Berichte, Dateien | [globale Suche](../web/global-search.md), [Berichte und Dashboards](reporting.md) im Browser gebaut, [Dateiablage](../modules/files.md) mit Vorschau |
| Benachrichtigungen, Mail, Sprachen | die Glocke mit Push und Sammelmail, Mailvorlagen, die Oberfläche in 23 [Sprachen](localization.md) |
| Admin-Oberfläche | Benutzer, Rollen, Einstellungen, Themes, Sprachen, Übersetzungen, Audit-Log, Backups, API-Schlüssel |
| MCP und API-Schlüssel | jede App ist ein [MCP-Server](../modules/mcp.md) unter `/mcp`, angemeldet mit persönlichen [API-Schlüsseln](../security/api-keys.md) |

Das CLI erzeugt Entities auf genau diesen Voreinstellungen. Jede Entity aus `coworkee entity add` oder aus einer Spec ist eine `AuditedEntity` mit `IMultiTenant` und `[Realtime]`, mit Rechten zum Ansehen, Anlegen, Bearbeiten und Löschen, einem Validator, Endpunkten, einem OData-Set und einer Blazor-Seite ([Module und Entities](../getting-started/cli.md#module-und-entities)). Was der Agent daneben schreibt, beginnt bei Code, der schon sicher ist.

## Ein Weg, ein Feature zu schreiben

Jeder Anwendungsfall ist ein Request, ein Handler und ein Validator, verschickt über einen Dispatcher. Ein Agent, der ein Modul gelesen hat, kennt alle, und [Das erste Feature](../getting-started/first-feature.md) zeigt das Muster an einer Entity. Diese Einheitlichkeit macht erzeugten Code prüfbar: Ein fehlendes Berechtigungsattribut oder ein Handler, der selbst speichert, fällt im Diff auf.

```csharp
[RequiresPermission(CatalogPermissions.Brands.Delete)]
public sealed record DeleteBrandCommand(Guid Id) : ICommand<Result>;
```

## Werkzeuge für den Agenten

**Regeln in der Solution.** Jede App bringt im Wurzelordner eine `AGENTS.md` mit, die Codex, Copilot, Cursor und die meisten anderen Agenten lesen, und eine `CLAUDE.md`, die sie für Claude Code einbindet. Sie nennt den Aufbau der Solution, wohin Entities, Requests und Seiten gehören, die Muster jedes Features (`AuditedEntity`, `IMultiTenant`, `[Realtime]`, `[RequiresPermission]` mit seiner Berechtigungsdefinition, Validatoren), die Fallstricke unten und die Befehle für Migrationen, Tests und das CLI. Sie folgt den Optionen der App: Ohne Beispiele zeigt sie nicht auf den Katalog, ohne Tests fehlt der Testabschnitt. Ergänze sie um die Regeln deiner Domäne.

**Das CLI als MCP-Server.** `coworkee mcp` gibt Claude Code, Claude Desktop, VS Code und anderen Clients die Tools `list_options`, `get_spec_schema`, `get_spec_example`, `validate_spec`, `create_app`, `add_module`, `add_entity`, `import_entities`, `add_migration`, `add_option`, `run_doctor`, `update_packages` und `search_docs`. Sie führen dieselben Befehle aus, die du tippen würdest. Siehe [MCP-Server des CLI](../getting-started/mcp.md).

```bash
claude mcp add coworkee -- coworkee mcp
```

**App-Specs.** Eine ganze App mit Optionen, Branding, Modulen und Entities ist eine JSON-Datei mit veröffentlichtem [JSON-Schema](../assets/spec.schema.json). Ein Agent schreibt sie, `coworkee spec validate` prüft sie, `coworkee new --spec` legt die App an. Coworkee Studio liest und schreibt dieselbe Datei, du kannst also öffnen, was der Agent geplant hat, und es von Hand ändern. Siehe [App-Spezifikationen](../getting-started/cli.md#app-spezifikationen) und die [Startvorlagen](../getting-started/gallery.md).

**Vorhersehbare Ausgabe.** `coworkee new` schreibt vor jedem Schritt `==> <Schritt>` und endet mit einem Exit-Code ungleich 0, wenn einer scheitert. Ein Agent sieht also, wo er stehen geblieben ist.

**Die laufende App als MCP-Server.** Läuft die App, kann ein Agent mit ihren Daten arbeiten: `/mcp` zeigt nur die Tools, die der Besitzer des API-Schlüssels nutzen darf, markiert jedes Command als destruktiv, damit der Client erst fragt, und schreibt jeden Aufruf ins Audit der KI-Tool-Aufrufe. Siehe [MCP-Server der App](../modules/mcp.md).

## Was deine Aufgabe bleibt

Die Sicherheit der Pakete deckt keinen Code ab, der um sie herum läuft. Prüf, was der Agent geschrieben hat, mit Blick auf diese Punkte:

- Ein Request ohne `[RequiresPermission]` wird nicht geprüft. Setz das Attribut an jeden Request, der nicht für alle gedacht ist.
- Eine Entity ohne `IMultiTenant` teilen sich alle Mandanten.
- `ExecuteUpdate` und `ExecuteDelete` umgehen den Change Tracker: kein Audit-Eintrag, keine Realtime-Nachricht.
- Datensätze, die nur manche Benutzer sehen dürfen, brauchen eigene Realtime-Topics.

`Coworkee.Testing` führt API-Tests gegen eine echte Datenbank aus. Ein Test, den der Agent schreibt, kann also belegen, dass ein Benutzer ohne die Berechtigung ein 403 bekommt und ein anderer Mandant nichts sieht. Siehe [Tests](../testing.md).
