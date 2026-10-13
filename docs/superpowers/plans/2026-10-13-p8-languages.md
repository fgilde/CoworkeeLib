# Paket 8 – Sprachen und Text-Export: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die Lib liefert 23 Sprachen vollständig (UI-, AuthServer- und Mail-Texte), ein Test erzwingt Vollständigkeit, die deutschen Texte sind sprachlich sauber, und `coworkee texts export` holt Lib-Texte gezielt in eine App.

**Architecture:** Übersetzungen als zusätzliche `{lang}.json` neben den vorhandenen `de.json` (gleiches Format: englischer Key → Text) und als `*.{lang}.subject|body.scriban`-Mailvorlagen. Englisch ist die Key-Sprache (keine `en.json`). Ein Test vergleicht jede Sprache mit der Key-Menge der `de.json` derselben Quelle und prüft Platzhalter. „Ausgeliefert“ heißt verfügbar ohne Maschinenübersetzung; standardmäßig aktiv bleiben weiterhin die per Konfiguration gesetzten Sprachen.

**Tech Stack:** JSON-Ressourcen (eingebettet), Scriban, xunit, CLI `Coworkee.Cli` (System.CommandLine o. ä. wie vorhandene Befehle).

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 8)

## Global Constraints

- Branch `feat/languages` von `main` **nach** dem Merge von Paket 6 und 7 (deren neue Texte müssen enthalten sein).
- Sprachen (Codes exakt): `ar, bg, cs, da, de, el, es, fr, hr, id, it, ja, km, ko, nl, pl, pt, ru, sv, tr, uk, zh` + `en` (Keys). `zh` = vereinfachtes Chinesisch, `pt` = brasilianisches Portugiesisch, Anrede wie in der jeweiligen Software-UI üblich (de: „Sie“ wie bestehend).
- Quellen: `src/Coworkee.Localization/Localization/de.json` (Lib-UI), `src/Coworkee.AuthServer/Texts/de.json` (Login-Seiten), `src/Coworkee.Mailing/Templates/*.de.{subject,body}.scriban` (Mails), `templates/Coworkee.Templates/content/CoworkeeApp/src/MyApp.Contracts/Localization/de.json` (Template-App).
- Platzhalter `{0}`, `{1}`, Scriban `{{ … }}`, HTML-Tags, Markdown, Zeilenumbrüche und führende/abschließende Leerzeichen bleiben exakt erhalten; Produktnamen (Coworkee, Keycloak, OpenID Connect, MCP, SMTP, …) bleiben unübersetzt.
- `de.json` hat Keys, die sich nur in Groß-/Kleinschreibung unterscheiden: JSON nie per `ConvertFrom-Json` round-trippen; Werkzeuge müssen case-sensitive arbeiten (z. B. .NET `JsonDocument`, Python `json` mit Dict).
- Keine Attribution in Commits; Docs EN + DE.

## Review Focus

- Key-Menge jeder Sprache == Key-Menge der `de.json` derselben Quelle (keine fehlenden, keine überzähligen, case-sensitive).
- Platzhalter-Multimenge pro Eintrag identisch zum Key (bzw. zur deutschen Vorlage bei Scriban).
- Rechts-nach-links (ar): nur Text übersetzt, keine Steuerzeichen eingefügt.
- Template-App mit `--languages de,fr` enthält genau diese Sprachdateien und baut.
- `coworkee texts export` überschreibt vorhandene App-Übersetzungen nicht (Merge) und schreibt valides JSON mit Unicode unescaped.

---

### Task 1: Infrastruktur + Vollständigkeitstest + deutsche Textqualität

**Files:**
- Test: `tests/Coworkee.Localization.Tests/ShippedLanguagesTests.cs` (neu): für Lib-UI-, AuthServer- und Template-App-Texte je Sprache aus `ShippedLanguages` (ohne `en`): Datei existiert, Keys == Keys der `de.json`, Platzhalter-Multimengen gleich; für Mailvorlagen: zu jeder `*.de.*.scriban` existiert `*.{lang}.*.scriban` mit denselben `{{ … }}`-Ausdrücken. Der Test liest Dateien aus dem Repo (Pfad über Solution-Root ermitteln, wie andere Repo-Tests es tun) oder aus den eingebetteten Ressourcen – vorhandenes Muster nutzen.
- Modify: `src/Coworkee.Cli.Spec/OptionCatalog.cs:54` `ShippedLanguages` → alle 23; Sprachkatalog um `km` ergänzen (prüfen, welche Codes fehlen). Prüfen, wer `ShippedLanguages` nutzt (Studio, CLI, `LocalizationOptions.cs:17` Default `["en", ...shipped]`): Default aktiver Sprachen darf **nicht** automatisch alle 23 werden → Default bleibt `["en", "de"]` (eigene Konstante), Studio/CLI zeigen „ausgeliefert“ korrekt.
- Prüfen/anpassen: Einbettung `*.json` je Projekt (`.csproj` Wildcards), `AuthTexts` (`src/Coworkee.AuthServer/AuthTexts.cs`) lädt beliebige Sprachcodes, Mail-Regex (`MailTemplates.cs:107`) akzeptiert alle Codes (z. B. `zh`, `km`).
- Template: wie wird die App-`de.json` heute eingebunden/gefiltert (`template.json` Parameter `languages`)? Mechanik so erweitern, dass genau die gewählten Sprachdateien in der neuen App landen (Konditionen in `template.json` `sources.modifiers` o. ä.).
- Deutsche Qualität: alle vier deutschen Quellen sprachlich prüfen und schlechte/maschinelle Übersetzungen korrigieren (Beispiele: „In einem Rollenhandel“, „…; 0 soll jedes.“). Bedeutung aus dem Code-Kontext des Keys ableiten (grep).
- Der Test ist zunächst rot (Dateien fehlen) – er wird mit dem Attribut/Trait so markiert, dass er erst nach Task 3 Pflicht ist? **Nein**: Task 1 committet den Test, Task 2 liefert die Dateien, der Branch wird erst gemergt, wenn er grün ist.

- [ ] Test schreiben, rot (fehlende Dateien) – Ausgabe im Report
- [ ] Infrastruktur, Template-Mechanik, de-Korrekturen; übrige Testprojekte grün
- [ ] Commit `test(localization): every shipped language is complete` + `fix(localization): German texts read naturally` + `feat(templates): new apps get the texts of the chosen languages`

### Task 2: Übersetzungen (je Sprache ein Subagent)

Pro Sprache `<lang>`:
- Create: `src/Coworkee.Localization/Localization/<lang>.json`, `src/Coworkee.AuthServer/Texts/<lang>.json`, `src/Coworkee.Mailing/Templates/*.<lang>.{subject,body}.scriban` (je deutsche Vorlage eine), `templates/Coworkee.Templates/content/CoworkeeApp/src/MyApp.Contracts/Localization/<lang>.json`.
- Quelle: englischer Key = Bedeutung; deutscher Wert als Kontext; bei Unklarheit Key im Code suchen.
- Prüfung: `ShippedLanguagesTests` für `<lang>` grün.
- Commit je Sprache `feat(localization): <Language name> texts` (Commits seriell durch den Controller, Subagenten schreiben nur Dateien).

### Task 3: `coworkee texts export`

**Interfaces:**
- CLI: `coworkee texts export --lang <code> [--keys <glob|regex>] [--out <path>]` – liest die eingebetteten Lib-Texte der Sprache (UI + AuthServer optional mit `--auth`), schreibt in `<out>` (Default: `src/<App>.Contracts/Localization/<code>.json` der App im aktuellen Verzeichnis, wie andere CLI-Befehle die App finden), Merge: vorhandene Keys bleiben, neue werden ergänzt, sortiert wie vorhanden. Englisch (`--lang en`) exportiert Key = Text.

**Files:**
- Create/Modify: Befehl im CLI nach vorhandenem Muster (`src/Coworkee.Cli/...`, z. B. wie `entity`/`module`-Befehle), Hilfe-Text, `src/Coworkee.Cli/README.md`.
- Test: `tests/Coworkee.Cli.Tests`: Export neu, Merge bewahrt vorhandene Werte, Filter `--keys`, unbekannte Sprache → Fehler mit Liste.
- Docs: `website/docs/modules/localization.md` (+de): ausgelieferte Sprachen, Override-Mechanik (gleicher Key in der App-JSON gewinnt; `/admin/translations` geht zur Laufzeit vor), Export-Befehl, Template-Verhalten.

- [ ] Failing tests → RED → implementieren → GREEN
- [ ] Commit `feat(cli): texts export copies library texts into an app for overriding`

### Task 4: Merge, Demo

- [ ] Voller Build + Tests grün, mkdocs strict, Merge, Pack; Demo: Sprache auf Französisch/Japanisch umschalten, Login-Seite + Admin prüfen.
