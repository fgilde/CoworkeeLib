# Paket 3 – Sticky-Merge von Tabs und ObjectEdit-Toolbar: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Beim Herunterscrollen auf Admin → Einstellungen verschmelzen Tab-Leiste und Sticky-Toolbar des ObjectEdits zu einer Leiste; beim Hochscrollen ist alles wie vorher.

**Architecture:** Wiederverwendbare Komponente `StickyMergeTabs` in der Lib (umschließt `MudTabs`, rendert ein Sentinel-Element und das CSS), JS-Funktion `observeStuck(sentinel, tabs)` in `wwwroot/coworkee.js` (IntersectionObserver setzt/entfernt Attribut `stuck` an `.mud-tabs-tabbar`). Vorbild: alte Lösung im Demo-Repo (`git show 2e4c65c` und Stand `b4eecad` in `C:\dev\privat\github\cleanarchitecturebaseblazor`: `Settings.razor`, `JsRuntimeExtensions.cs` `ObserveMudTabsForStickMerge`, `site.scss` `.mud-tabs-stick-merge`).

**Tech Stack:** Blazor WASM, MudBlazor 9.11 `MudTabs`, MudEx `MudExObjectEditForm` (`StickyToolbar`, `StickyToolbarTop` Default `var(--mud-appbar-height)`), bUnit.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 3)

## Global Constraints

- Branch `feat/sticky-merge-tabs` von `main`; nach grünem Build + Tests Merge nach `main`.
- CCD, `.razor` + `.razor.cs`, keine erklärenden Kommentare außer nicht offensichtlichem Warum. Kein `eval`-Interop.
- Lib hat keine CSS-Datei; Komponenten-CSS wie im Bestand als `<style>` in der Komponente (z. B. `ThemePicker.razor`, `AboutDialog.razor`) oder `.razor.css`, falls das Projekt Scoped CSS bündelt (prüfen).
- Keine Attribution in Commits.

## Review Focus

- Gemergte Tab-Leiste überdeckt keine Toolbar-Aktionen (Breite = Breite der Tabs, `fit-content`, nicht fix 450px); Toolbar-Aktionen sitzen rechts.
- Tab ohne Sticky-Toolbar (System, Organisation: `SettingsForm` hat nur `StickyActionBar`) – Tab-Leiste klebt allein, nichts springt.
- Mobile (schmal): Tabs + Toolbar passen nicht nebeneinander → kein Überlappen (unter `sm` kein Merge, nur sticky Tabs).
- Dark/Light: Hintergrund der gemergten Leiste deckend (`--mud-palette-surface`), z-index über Toolbar, unter AppBar/Popovers.
- Observer wird beim Verlassen der Seite getrennt (Dispose).

---

### Task 1: `StickyMergeTabs` + Einsatz auf der Settings-Seite

**Files:**
- Create: `src/Coworkee.Client.Blazor/Components/StickyMergeTabs.razor` (+ `.razor.cs`): Parameter `ChildContent` (Tab-Panels), `Class`, alle übrigen per `[Parameter(CaptureUnmatchedValues = true)]` an `MudTabs` durchgereicht; rendert Sentinel `<div>` direkt vor `MudTabs` mit Klasse `cw-tabs-stick-merge`; ruft nach erstem Render `observeStuck` und disposed das zurückgegebene Handle.
- Modify: `src/Coworkee.Client.Blazor/wwwroot/coworkee.js`: `export function observeStuck(sentinel, tabs)` → `{ dispose }`.
- Modify: `src/Coworkee.Client.Blazor/Pages/Admin/Settings.razor`: `MudTabs` → `StickyMergeTabs` (gleiche Parameter).
- Test: `tests/Coworkee.Client.Blazor.Tests` (bUnit, JSInterop im Loose/Setup-Modus wie in vorhandenen Tests): rendert Panels, ruft `observeStuck` genau einmal auf, ruft beim Dispose `dispose`.
- Docs: kurzer Abschnitt in der passenden Komponenten-/UI-Seite unter `website/docs` (EN + DE).

- [ ] Failing bUnit-Test, RED
- [ ] Implementieren (CSS: `.cw-tabs-stick-merge .mud-tabs-tabbar { position: sticky; top: var(--mud-appbar-height); z-index: …; }` und `[stuck]` → `width: fit-content; background: var(--mud-palette-surface)`; unter 600px nur sticky ohne Breitenänderung)
- [ ] GREEN, Client.Blazor-Tests grün, mkdocs strict grün
- [ ] Commit `feat(ui): tab bar and the object edit toolbar merge into one sticky bar while scrolling`
- [ ] Browser-Prüfung durch Controller (Demo)
