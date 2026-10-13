# Paket 9 – Settings-Feinschliff (Owner-Feedback): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Alle ObjectEdits auf Admin → Einstellungen sind gleich konfiguriert und erlauben mehrere offene Gruppen; gesperrte Felder sind als gesperrt erkennbar; riskante Felder sind änderbar, aber geschützt; die Demo-Währung ist eine Auswahlliste; der Tab-Kopf passt im Sticky-Zustand sauber.

**Architecture:** Zwei Stufen in den Regeln der App-Konfiguration (`AppConfigurationRules`, `src/Coworkee.Settings/AppConfigurationRegistration.cs`): bestehendes `Lock` (Server lehnt Änderungen ab) und neues `Protect` (Server erlaubt, UI schützt per MudEx `WithEditConfirmation(ConfirmationProtection.ToggleButton(...))`). Gemeinsame ObjectEdit-Konfiguration für `AppConfigurationEditor` und `SettingsForm` an einer Stelle.

**Tech Stack:** Blazor WASM, MudEx 9.11.2 ObjectEdit (`SingleExpand`, `WithEditConfirmation`, `ConfirmationProtection`), Nextended `Currency.All`.

**Spec:** Owner-Feedback 2026-10-13 (im Chat); ergänzt Paket 1/3 des Designs `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md`.

## Global Constraints

- Arbeitsverzeichnis: Worktree `C:\dev\privat\github\wt\p9`, Branch `feat/settings-polish` (nicht das Haupt-Repo).
- Demo-Repo `C:\dev\privat\github\cleanarchitecturebaseblazor` (Branch master) nur für Task 3; Demo baut gegen lokal gepackte Lib (`../coworkee/artifacts/nuget`), Lib-Änderungen erst nach Merge/Pack sichtbar – Demo-Code so schreiben, dass er gegen die neue API kompiliert (Controller packt).
- CCD, `.razor` + `.razor.cs`, keine erklärenden Kommentare außer nicht offensichtlichem Warum; keine Attribution; Texte EN-Key + `de.json`; Docs EN + DE.
- Lib-Felder: `Jobs.ConnectionStringName`, `Jobs.RunServer`, `Jobs.DashboardPath`, `Notifications.PublicAppUrl` wechseln von `Lock` zu `Protect`. `Jobs.Queues`, `Jobs.RetryDelaysInSeconds` bleiben `Hide`.

## Review Focus

- `Lock` bleibt serverseitig durchgesetzt (Änderung wird abgelehnt/ignoriert wie heute); `Protect` ist nur UI-Schutz, Server speichert.
- Gesperrtes Feld: deaktiviert (nicht nur readonly), Schloss-Symbol + Tooltip „Set in the configuration“; bei Objekten/Listen alle Kinder.
- Geschütztes Feld: Bearbeiten erst nach Entsperren; nach Speichern/Neuladen wieder geschützt.
- `SingleExpand=false` überall; Suchfilter/Export/Import funktionieren auch im System-/Organisation-Tab (Export/Import dort nur, wenn sinnvoll – Secrets nie exportieren).
- Sticky-Tab-Kopf: Innenabstand/Höhe passend zur ObjectEdit-Toolbar, kein Überlappen von Aktionen, Light/Dark.

---

### Task 1: Einheitliche ObjectEdit-Konfiguration + Mehrfach-Expand + Tab-Kopf

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Components/AppConfigurationEditor.razor`, `src/Coworkee.Client.Blazor/Components/SettingsForm.razor` (gleiche Parameter: `SingleExpand="false"`, sticky Toolbar + ActionBar, Suche, Toolbar-Farbe/Ausrichtung, Grouping-Stil, Reset-Einstellungen; Export/Import im SettingsForm nur ohne Secrets oder weglassen – entscheiden und im Report begründen). Gemeinsame Werte an einer Stelle (z. B. statische Defaults in `SettingsFormRules` oder kleine Komponente), nicht doppelt.
- Modify: `src/Coworkee.Client.Blazor/Components/StickyMergeTabs.razor` (CSS `[stuck]`: Padding/Höhe/Rundung an Toolbar angleichen; im Browser prüft der Controller).
- Test: bUnit – beide Formulare rendern mit `SingleExpand == false` (Instanz-Parameter prüfen) und mit Toolbar-Suche.

- [ ] Failing tests → RED → implementieren → GREEN
- [ ] Commit `fix(settings): every settings form shares one setup and keeps several groups open`

### Task 2: `Protect` neben `Lock`, gesperrte Felder sichtbar gesperrt

**Interfaces:**
- Produces: `AppConfigurationRules<T>.Protect(params Expression<Func<T, object?>>[] properties)` (gleiches Muster wie `Lock`/`Hide`); DTO `AppConfigurationValuesDto` bekommt `IReadOnlyList<string>? Protected = null` (optional am Ende).
- UI (`AppConfigurationEditor.Configure`): `Locked` → `AsDisabled` (bzw. Disabled-Attribut) + Schloss-Adornment/Icon + Tooltip/Beschreibungszusatz „Set in the configuration.“; `Protected` → `WithEditConfirmation(ConfirmationProtection.ToggleButton(L["Unlock to change; applies after a restart."]))` (genaue MudEx-API prüfen: `ConfirmationProtection`-Fabrik, ToggleButton-Variante mit Lock/LockOpen-Icons).

**Files:**
- Modify: `src/Coworkee.Settings/AppConfigurationRegistration.cs` (Regel + Lib-Felder umstellen), `src/Coworkee.Settings/AppConfiguration.cs` (Protected-Pfade ausliefern), `src/Coworkee.Contracts/Settings/SettingsContracts.cs`, `src/Coworkee.Client.Blazor/Components/AppConfigurationEditor.razor.cs`, `de.json`.
- Docs: `website/docs/modules/settings.md` (+de): Lock vs. Protect.
- Test: Settings.Tests (Protect-Felder speicherbar, Lock-Felder nicht; DTO liefert Protected), bUnit (gesperrtes Feld disabled + Hinweis; geschütztes Feld erst nach Entsperren editierbar).

- [ ] Failing tests → RED → implementieren → GREEN
- [ ] Commit `feat(settings): protected settings unlock before editing; locked ones show they come from the configuration`

### Task 3: Demo – Währung als Auswahl, Protect statt Lock

**Files (Demo-Repo):**
- `src/MyApp.Infrastructure/MyAppInfrastructureModule.cs:27`: `rules.Lock(s => s.Catalog.Currency)` → `rules.Protect(...)`.
- `src/MyApp.Web.Client/Program.cs:37`: Meta für `Catalog.Currency` → Auswahl (`MudExSelect<string>`) über `Nextended.Core.Types.Currency.All` (ISO-Code als Wert, Anzeige „EUR – Euro“), danach Spalten-Wrap (bekannter MudEx-`RenderWith`-Bug: Wrapper geht verloren; Lib-`SettingsFormRules.WrapInColumns` wird nach der Registrierungs-Meta schon aufgerufen – prüfen).
- Gleiches im CLI-Template, falls es dieselbe Catalog-Einstellung enthält (`templates/Coworkee.Templates/...` grep `Currency`).

- [ ] Umsetzen (Controller packt Lib vorher), Demo baut
- [ ] Commit im Demo-Repo `feat: currency is a choice of the known currencies and protected instead of locked`
