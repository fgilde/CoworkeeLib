# Paket 4 – MudEx: Cron-Editor und ObjectEdit-Fixes: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MudBlazor.Extensions bekommt einen an `string` bindbaren Cron-Editor mit Sample-Seite, und zwei ObjectEdit-Fehler, die Coworkee umgehen musste, werden an der Wurzel behoben.

**Architecture:** Alles im MudEx-Repo `C:\dev\privat\github\MudBlazor.Extensions` auf lokalem Branch `feat/coworkee-p4` (von `main`, **nie pushen**, Owner released). Coworkee nutzt weiter das Paket 9.11.2; die Umstellung der Lib erfolgt nach dem MudEx-Release (Follow-up in `CONTINUE.md`).

**Tech Stack:** MudBlazor, MudEx (net8/9/10), Sample-App `Samples/MainSample.WebAssembly`.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 4; Paket 1 Ledger: MudEx-Ursachen)

## Global Constraints

- MudEx-Konventionen strikt folgen (Benennung `MudEx…`, Ordner, XML-Doku wie Nachbarkomponenten, Lokalisierung über den MudEx-Mechanismus `LocalizerToUse`/`IStringLocalizer`, Sample-Seiten-Aufbau wie `Page_TagField.razor`, Navigation-Eintrag wie andere Komponenten).
- Keine Attribution in Commits; Conventional Commits englisch. Nie pushen.
- Build: Solution des MudEx-Repos; falls Testprojekt vorhanden, Tests ergänzen.

## Review Focus

- Ungültiger Cron-Ausdruck im gebundenen String: Editor zeigt Fehler, überschreibt den Wert nicht stillschweigend.
- Ausdruck, den die Frequenz-UI nicht abbilden kann (z. B. `*/7 3-5 * * 1`): bleibt unverändert, Freitextfeld zeigt ihn, Frequenz „Benutzerdefiniert“.
- Zeit-Popup in voller Größe (kein `Style` am Picker, der ins Popover gereicht wird).
- `RenderWith` nach `WrapEachInMudItem`/`WrapIn`: Wrapper bleibt erhalten.
- `AsDisabledIf`/`WithAttributesIf`: Bedingung wird false → Attribut wird auf seinen Default zurückgesetzt (Feld wieder aktiv), nicht nur entfernt.

---

### Task 1: ObjectEdit – Wrapper bei `RenderWith` behalten, bedingte Attribute zurücksetzen

**Files:**
- Modify: `MudBlazor.Extensions/Components/ObjectEdit/MudExObjectEditExtensions.PropertyMeta.cs:126-221` (alle `RenderWith`-Überladungen, die `RenderData` ersetzen: vorhandenen `Wrapper` auf neue RenderData übernehmen – an einer Stelle, nicht in jeder Überladung kopiert).
- Modify: `MudBlazor.Extensions/Components/ObjectEdit/RenderData.base.cs:293-304` (`AddAttributesIf`: im false-Zweig nicht nur entfernen, sondern den Wert wiederherstellen, den das Attribut vorher hatte bzw. – falls nicht gesetzt – den Default des Parameters, damit Blazor den Parameter wirklich zurücksetzt).
- Test: vorhandenes Testprojekt nutzen (suchen); sonst Sample-Seite/Repro im Report beschreiben.

- [ ] Failing tests (Wrapper bleibt nach `RenderWith`; `AsDisabledIf` → true → false: Komponente `Disabled == false`)
- [ ] Implementieren
- [ ] Tests/Build grün
- [ ] Commit `fix(objectedit): render with keeps the wrapper; conditional attributes reset when the condition turns false`

### Task 2: `MudExCronEdit`

**Files:**
- Create: `MudBlazor.Extensions/Components/MudExCronEdit.razor` + `.razor.cs` (+ Hilfsklasse für Parse/Format, portiert aus Coworkee `src/Coworkee.Client.Blazor/Components/Editors/CronEditor.razor(.cs)` und `CronSchedule.cs` – Funktionsumfang: Frequenz Minuten/Stündlich/Täglich/Wöchentlich/Monatlich/Benutzerdefiniert, Intervall, Minute, Uhrzeit, Wochentage als Chips, Tag im Monat, Freitext-Ausdruck mit Validierung und lesbarer Beschreibung). Parameter: `Value`/`ValueChanged` (string), `Label`, `HelperText`, `ReadOnly`, `Disabled`, `Variant`/`Margin`/`Dense` nach MudEx-Muster, `TimeIsUtc`-Hinweis optional (Label-Suffix). Popup-Bug nicht übernehmen.
- Optional (wenn MudEx-Muster vorhanden): ObjectEdit-Registrierung für ein Attribut (`[CronExpression]` o. ä.), damit string-Properties damit gerendert werden.
- Create: Sample-Seite `Samples/MainSample.WebAssembly/Pages/Page_CronEdit.razor` (wie `Page_TagField.razor`) mit gebundenem String und Anzeige des Werts; Navigationseintrag + ggf. Komponenten-Doku-Eintrag wie bei anderen Komponenten.
- Test: Unit-Tests für Parse/Format (Coworkee hat `tests/Coworkee.Client.Blazor.Tests/CronScheduleTests.cs` – Fälle übernehmen) im MudEx-Testprojekt, falls vorhanden.

- [ ] Failing tests (portierte CronSchedule-Fälle)
- [ ] Implementieren + Sample
- [ ] Tests/Build grün
- [ ] Commit `feat: MudExCronEdit, a cron expression editor bound to a string`

### Task 3: Follow-up in Coworkee vermerken

- [ ] `CONTINUE.md` (Coworkee) „Known follow-ups“: nach MudEx-Release (a) `[Cron]` auf `MudExCronEdit` umstellen und eigenen `CronEditor`/`CronSchedule` löschen, (b) Workarounds entfernen: `SettingsModel` zwei `WithAttributesIf` → `AsDisabledIf`, `SettingsFormRules.WrapInColumns`-Nachaufrufe nach `RenderWith`.
