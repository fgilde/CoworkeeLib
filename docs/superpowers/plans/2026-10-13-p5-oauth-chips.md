# Paket 5 – Mehrfachwerte der OAuth-Apps als Chips: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scopes, Redirect-URIs und Post-Logout-URIs einer OAuth-App (und Ressourcen eines Scopes) werden als Chip-Listen mit Freitext bearbeitet; Scopes bekommen Vorschläge (Standard-Scopes + angelegte Scopes).

**Architecture:** Neue generische Lib-Komponente `ChipListEditor` (Chips + `MudAutocomplete` mit Freitext). Ruling: `MudExSelect` kann keinen Freitext, `MudExTagField` keine Vorschläge; damit die Lib nicht auf ein MudEx-Release wartet und keine Doppelarbeit entsteht, lebt der Editor in der Lib (kann später nach MudEx wandern). Formmodelle bekommen `List<string>` statt Trenner-Strings; die API (`IReadOnlyList<string>`) bleibt.

**Tech Stack:** Blazor WASM, MudBlazor 9.11 (`MudAutocomplete`, `MudChip`), MudEx ObjectEdit (`RenderWith`), bUnit.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 5)

## Global Constraints

- Branch `feat/oauth-chips` von `main`; Merge nach `main` wenn grün.
- CCD, `.razor` + `.razor.cs`, keine erklärenden Kommentare außer nicht offensichtlichem Warum. Keine Attribution.
- Neue Texte: EN-Key + `de.json`.
- Validierung wie Server (`src/Coworkee.AuthServer/Clients/ClientHandlers.cs:29-51`): Scopes `^[A-Za-z0-9._:-]+$`, URIs absolut. `openid` bleibt erzwungen (wie heute in `ClientForm.From`).
- Standard-Scopes: `openid profile email roles offline_access phone address` (vgl. `ScopeHandlers.cs:25` `Standard`).

## Review Focus

- Eingabe mit Leerzeichen/Komma/Zeilenumbruch (Einfügen einer alten Liste „openid profile email“) → wird in mehrere Chips zerlegt.
- Duplikate (auch nach Trim, Scopes case-sensitive wie Server) → nicht doppelt.
- Ungültiger Wert (relative URI, Scope mit Leerzeichen-Sonderzeichen) → kein Chip, Fehlermeldung am Feld; bereits gespeicherte ungültige Werte werden angezeigt, nicht still verworfen.
- Fokusverlust mit getipptem, nicht bestätigtem Text → Text wird als Chip übernommen (sonst geht Eingabe beim Speichern verloren).
- `openid` als Chip nicht entfernbar.

---

### Task 1: `ChipListEditor`

**Interfaces:**
- Produces: `public partial class ChipListEditor` in `src/Coworkee.Client.Blazor/Components/Editors/ChipListEditor.razor(.cs)`:
  - `[Parameter] IReadOnlyList<string> Value`, `EventCallback<IReadOnlyList<string>> ValueChanged`
  - `string? Label`, `string? HelperText`, `string? Placeholder`, `bool ReadOnly`
  - `Func<string, CancellationToken, Task<IEnumerable<string>>>? Suggestions` (gefiltert nach Eingabe; bereits gewählte ausgeblendet)
  - `Func<string, string?>? Validate` (Fehlertext oder null)
  - `IReadOnlyCollection<string>? Fixed` (Chips ohne Schließen-Knopf)
- Verhalten: Enter, Komma, Leerzeichen-Paste und Auswahl eines Vorschlags fügen hinzu; Text wird an Leerzeichen/Komma/Zeilenumbruch zerlegt; Blur übernimmt getippten Text; Duplikate ignoriert; Chip-Schließen entfernt; ungültige Eingabe bleibt im Feld mit Fehlertext.

**Files:**
- Create: `ChipListEditor.razor`, `ChipListEditor.razor.cs`
- Test: `tests/Coworkee.Client.Blazor.Tests/ChipListEditorTests.cs`

- [ ] Failing tests: `Enter_adds_a_chip`, `Pasted_list_becomes_several_chips`, `Duplicates_are_ignored`, `Invalid_text_shows_the_error_and_adds_nothing`, `Closing_a_chip_removes_it`, `Fixed_chips_cannot_be_closed`, `Suggestions_exclude_chosen_values`, `Leaving_the_field_keeps_typed_text`
- [ ] RED → implementieren → GREEN
- [ ] Commit `feat(ui): chip list editor with free text and suggestions`

### Task 2: OAuth-App- und Scope-Formular

**Interfaces:**
- Consumes: `ChipListEditor` aus Task 1 (Rendering in der ObjectEdit-Meta via `RenderWith<ChipListEditor, …>(e => e.Value, e => { … })`; Property-Typ `List<string>`, ggf. Konverter zu `IReadOnlyList<string>`). Nach `RenderWith` behält das Grid seinen Wrapper nur, wenn danach erneut gewrappt wird – prüfen, wie `EditDialogExtensions` wrappt (P1-Erkenntnis: MudEx-`RenderWith` verwirft den Wrapper bis zum MudEx-Release).

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Pages/Admin/Clients.razor.cs` (`ClientForm`: `RedirectUris`, `PostLogoutRedirectUris`, `Scopes` → `List<string>`; `From`/`ToRequest` ohne Zeilen-/Leerzeichen-Split; Meta: Chips, Scopes mit Vorschlägen = Standard-Scopes ∪ Namen aus vorhandener Scopes-API, `openid` fixed; URIs mit `Validate` absolute URI; Hilfetexte anpassen)
- Modify: `src/Coworkee.Client.Blazor/Pages/Admin/Scopes.razor.cs` (`ScopeForm.Resources` → `List<string>`, Chips, Vorschläge = Ressourcen anderer Scopes)
- Modify: `de.json` (neue/geänderte Hilfetexte; alte Texte „One address per line“, „Separated by spaces…“ entfernen, falls nicht mehr genutzt)
- Test: vorhandene Client-/Scope-Admin-Tests in `tests/Coworkee.Client.Blazor.Tests` anpassen/ergänzen (z. B. `Saving_a_client_sends_the_chips_as_lists`, `Scope_suggestions_include_the_standard_and_the_defined_scopes`, `openid_is_always_part_of_the_scopes`)

- [ ] Failing tests → RED → implementieren → GREEN (Client.Blazor + AuthServer-Tests grün)
- [ ] Commit `feat(auth): scopes and addresses of OAuth apps are chip lists with suggestions`

### Task 3: Docs, Demo, Merge

- [ ] Docs zur Clients-Verwaltung (EN + DE) Screenshot-frei kurz anpassen; mkdocs strict.
- [ ] Merge, Pack, Demo im Browser: Admin → OAuth-Apps → Bearbeiten.
