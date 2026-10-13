# Paket 1 – Settings aufräumen: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admin → Settings ist stabil (kein Dauer-Reload, keine Falschmaskierung, Cron-Popup sichtbar), abhängige Felder werden deaktiviert, AI-Gruppe verschwindet aus dem System-Tab, Standardsprache ist ein Select.

**Architecture:** Bug-Fixes an der Wurzel (`AppConfigurationEditor`, `AppConfiguration.SecretName`, `CronEditor`). Zwei generische Erweiterungen des Settings-Systems: `SettingDefinition.EnabledWhen` (Bedingung auf ein anderes Setting) und `SettingGroup.Page` (Gruppe wird auf eigener Seite gepflegt, `SettingsForm` blendet sie aus). Neuer `SettingType.Language` rendert ein Sprach-Select.

**Tech Stack:** .NET 10, Blazor WASM, MudBlazor 9.11, MudBlazor.Extensions 9.11.2 (ObjectEdit), bUnit, xunit, Shouldly, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 1)

## Global Constraints

- Branch `fix/settings-cleanup` von `main`; nach grünem Build + Tests Merge nach `main`.
- CCD, `.razor` + `.razor.cs`, kleine Dateien, keine erklärenden Kommentare außer nicht offensichtlichem Warum; `// ponytail:` für bewusste Abkürzungen.
- Keine Attribution in Commits. Commit-Messages Conventional Commits, englisch.
- Neue UI-Texte: englischer Key + deutscher Eintrag in `src/Coworkee.Localization/Localization/de.json`.
- Docs EN + `.de.md` (echte Umlaute), nie „ABP“/„CleanArchitectureBase“ erwähnen.
- Build: `dotnet build Coworkee.slnx -c Release`; Tests: `dotnet test --solution Coworkee.slnx -c Release --no-build` (Docker nötig) bzw. gezielt `dotnet test tests/<Projekt> -c Release --filter <Name>`.

## Review Focus

- Speichern im App-Einstellungen-Tab lädt weiterhin neu (SaveAsync → LoadAsync) – der Guard darf nur das Parameter-Neuladen verhindern.
- Ein echtes Secret-Feld `ConnectionString` und verschachtelte Keys (`Db:ConnectionString`) bleiben maskiert und beim Zurücksenden der Maske erhalten (`AppConfiguration.cs:241`).
- `EnabledWhen` auf ein Setting, das im Scope nicht vorhanden ist (z. B. Tenant-Form): Feld bleibt aktiv, keine Exception.
- Sprach-Select im Tenant-/User-Scope muss „leer“ (= erben) erlauben; im Global-Scope ebenfalls leer = Konfigurations-Default.
- Benutzer-Einstellungen (Scope User, gleiche `SettingsForm`) dürfen durch `Page`-Filter keine eigenen Gruppen verlieren, die sie heute zeigen (Sprache, Darstellung, Digest).

---

### Task 1: Kein Neuladen bei gleichen Parametern

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Components/AppConfigurationEditor.razor.cs:40`
- Modify: `src/Coworkee.Client.Blazor/Components/SettingsForm.razor.cs:33`
- Test: `tests/Coworkee.Client.Blazor.Tests/AppConfigurationEditorTests.cs`, `tests/Coworkee.Client.Blazor.Tests/SettingsFormTests.cs`

- [ ] **Step 1: Failing tests**

`AppConfigurationEditorTests.Loads_once_while_the_section_stays`: Editor rendern, dann `editor.SetParametersAndRender(p => p.Add(e => e.Registration, Registration with { }))` zweimal; `await Api.Received(1).GetAppConfigurationAsync(Registration.Section, Arg.Any<CancellationToken>())`.
`SettingsFormTests.Loads_once_while_the_scope_stays`: Form rendern, `SetParametersAndRender(p => p.Add(f => f.Scope, SettingScope.Global))` zweimal; `Api.Received(1).GetSettingsAsync(SettingScope.Global, …)`.

- [ ] **Step 2: Tests laufen lassen → FAIL** (`Received 3`).

- [ ] **Step 3: Implementieren** – Feld `_loadedSection` (`string?`) bzw. `_loadedScope` (`SettingScope?`); `OnParametersSetAsync` lädt nur, wenn Wert abweicht. `SaveAsync`/`ResetAsync` laden weiter direkt.

- [ ] **Step 4: Tests → PASS**, ganze Testklassen grün.

- [ ] **Step 5: Commit** `fix(settings): editors load once per section instead of on every parent render`

### Task 2: Maskierung nur für Secret-Namen

**Files:**
- Modify: `src/Coworkee.Settings/AppConfiguration.cs:392`
- Test: vorhandene AppConfiguration-Tests in `tests/Coworkee.Settings.Tests` (Datei per `grep -l AppConfigurationMask tests/Coworkee.Settings.Tests`)

- [ ] **Step 1: Failing test** `Masks_secret_names_only`: Sektion mit `Jobs:ConnectionStringName = "default"`, `Db:ConnectionString = "Host=x"`, `Mail:Password = "p"`, `Ai:ApiKey = "k"`, `Auth:ClientSecret = "s"`, `Ai:MaxOutputTokens` (String-Wert "100"). Erwartung: `ConnectionStringName` und `MaxOutputTokens` im Klartext, die anderen `AppConfigurationMask.Value`.
- [ ] **Step 2: FAIL**
- [ ] **Step 3:** Regex auf Namensende verankern: `(password|secret|apikey|api_key|token|connectionstring)$`, `IgnoreCase`.
- [ ] **Step 4: PASS** + bestehende Mask-/Keep-Secret-Tests grün.
- [ ] **Step 5: Commit** `fix(settings): only names ending in a secret word are masked`

### Task 3: Cron-Uhr nicht abgeschnitten

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Components/Editors/CronEditor.razor:30-31` (+ ggf. `CronEditor.razor.css` oder vorhandene Lib-CSS)

- [ ] **Step 1:** `Style="max-width:140px"` am `MudTimePicker` entfernen; Breite über `Class="cw-cron-time"` mit CSS `.cw-cron-time { max-width: 140px; }` an der Stelle, wo die Lib Komponenten-CSS ablegt (vorhandenes Muster suchen: `grep -r "cw-" src/Coworkee.Client.Blazor/wwwroot`). MudBlazor reicht `Class` nicht an das Popover-Paper weiter (prüfen in MudBlazor-Quellen `MudPicker.razor.cs` `PickerPaperClassname`; falls doch, Wrapper-`div` mit der Klasse).
- [ ] **Step 2:** bestehende Cron-Tests grün (`--filter Cron`).
- [ ] **Step 3: Commit** `fix(settings): time picker popup of the cron editor gets its full width`

### Task 4: `EnabledWhen` – abhängige Settings

**Interfaces:**
- Produces: `public sealed record SettingCondition(string Setting, IReadOnlyList<string> Values);` in `src/Coworkee.Contracts/Settings/SettingsContracts.cs`. `SettingDefinition` und `SettingDefinitionDto` erhalten letzten optionalen Parameter `SettingCondition? EnabledWhen = null`. `SettingGroupBuilder.Add(..., string[]? choices = null, SettingCondition? enabledWhen = null)`. Hilfs-Fabrik `SettingCondition.IsTrue(string setting) => new(setting, ["true"])`.

**Files:**
- Modify: `src/Coworkee.Contracts/Settings/SettingsContracts.cs`, `src/Coworkee.Settings/SettingDefinitions.cs`, `src/Coworkee.Settings/SettingHandlers.cs:43`, `src/Coworkee.Client.Blazor/Components/SettingsModel.cs` (Configure)
- Modify: `src/Coworkee.Settings/FilePickerSettingDefinitions.cs` (ClientId/AppKey ← jeweiliges `…Enabled`), `src/Coworkee.Localization/Settings/LocalizationSettingDefinitions.cs` (TranslatorKey ← Provider in deepL/azure/google/libreTranslate; Region ← azure; Endpoint ← libreTranslate/deepL/azure/google wie Beschreibung; Email ← myMemory; Konstanten aus `TranslationProviders`)
- Test: `tests/Coworkee.Client.Blazor.Tests/SettingsFormTests.cs`, Handler-Test in `tests/Coworkee.Settings.Tests` (DTO trägt `EnabledWhen`)

- [ ] **Step 1: Failing tests**
  - `SettingsFormTests.A_setting_is_disabled_while_its_condition_fails`: Gruppe mit `Feature.On` (Bool, Default "false") und `Feature.Key` (String, `EnabledWhen = SettingCondition.IsTrue("Feature.On")`). Erwartung: Input „Key“ hat `disabled`; nach `Input(form,"On").Change(true)` → `WaitForAssertion` Input „Key“ ohne `disabled`.
  - `SettingsFormTests.A_condition_on_a_missing_setting_keeps_the_field_enabled`.
  - Settings-Test: `GET /api/v1/settings/definitions` liefert `enabledWhen` für `FilePickers.GoogleDrive.ClientId` mit `Setting = FilePickers.GoogleDrive.Enabled`.
- [ ] **Step 2: FAIL**
- [ ] **Step 3: Implementieren** – in `SettingsModel.Configure`: wenn `setting.EnabledWhen` gesetzt und die Bedingungs-Property im Typ existiert: `property.AsDisabledIf<object>(m => !Values.Contains(Format(prop.GetValue(m))))` (MudEx `AsDisabledIf`, `MudExObjectEditExtensions.PropertyMeta.cs:639`). Wertet MudEx die Bedingung beim Ändern der Checkbox nicht neu aus (Test zeigt es), Ursache in MudEx suchen; Fix im MudEx-Branch `feat/coworkee-p4` (lokal, nie pushen) und in der Lib mit vorhandenem Mechanismus `UpdateAllConditionalSettings()` (siehe `EditDialogExtensions.cs:111`) nach Wertänderung überbrücken.
- [ ] **Step 4: PASS**
- [ ] **Step 5: Commit** `feat(settings): settings can depend on another setting and are disabled while it does not match`

### Task 5: Gruppen auf eigener Seite; AI aus System-Tab, MCP auf AI-Seite

**Interfaces:**
- Produces: `public sealed record SettingGroup(string Name, string DisplayName, string? Page);` in `src/Coworkee.Settings/SettingDefinitions.cs`; `SettingDefinitionContext.Group(string name, string displayName, string? page = null)`; `ISettingDefinitionManager.Groups` → `IReadOnlyList<SettingGroup>`. `SettingGroupDto(string Name, string DisplayName, IReadOnlyList<SettingDefinitionDto> Settings, string? Page = null)`.

**Files:**
- Modify: `src/Coworkee.Settings/SettingDefinitions.cs`, `src/Coworkee.Settings/SettingHandlers.cs`, `src/Coworkee.Contracts/Settings/SettingsContracts.cs`, `src/Coworkee.Client.Blazor/Components/SettingsForm.razor.cs:39-42`, `src/Coworkee.Ai/CoworkeeAiModule.cs:161` (`page: "/admin/ai-settings"`)
- Modify: `src/Coworkee.Client.Blazor/Ai/AiConnectionForm.cs` (+ `McpEnabled` lesen/schreiben), `src/Coworkee.Client.Blazor/Pages/Admin/AiConfiguration.razor` (eigener `MudPaper`-Block mit Switch „MCP server enabled“ + Beschreibung, analog „Assistant enabled“, `data-testid="ai-mcp-enabled"`)
- Test: `tests/Coworkee.Client.Blazor.Tests/SettingsFormTests.cs`, `tests/Coworkee.Client.Blazor.Tests/AiConfigurationTests.cs`

- [ ] **Step 1: Failing tests**
  - `SettingsFormTests.Groups_with_an_own_page_are_not_shown`: Gruppe `("Ai","AI",[…], Page: "/admin/ai-settings")` → Labels enthalten kein Ai-Feld.
  - `AiConfigurationTests.Mcp_switch_saves_the_setting`: Switch `ai-mcp-enabled` umschalten, Save → `SetSettingsAsync(Global, dict mit "Mcp.Enabled" = "false")`.
- [ ] **Step 2: FAIL**
- [ ] **Step 3: Implementieren** (alle Aufrufer von `Groups` anpassen; `SettingsForm` filtert `g.Page is null`).
- [ ] **Step 4: PASS**
- [ ] **Step 5: Commit** `feat(settings): groups with an own page stay off the settings tabs; MCP switch on the AI page`

### Task 6: Standardsprache als Select

**Interfaces:**
- Produces: `SettingType.Language` (neuer Enum-Wert am Ende von `SettingType`).

**Files:**
- Modify: `src/Coworkee.Contracts/Settings/SettingsContracts.cs`, `src/Coworkee.Localization/Settings/LocalizationSettingDefinitions.cs:13-14` (Gruppe „Localization“ Anzeige „Language“, Setting DisplayName „Default language“, Type `Language`, Description „The language the app starts in. An organisation overrides the system, a user overrides both; empty inherits.“), `src/Coworkee.Client.Blazor/Components/SettingsModel.cs` (Rendering: `MudExSelect<string>` über die Sprachen des Clients wie `src/Coworkee.Client.Blazor/Pages/Admin/UserFormMeta.cs:25-29`, inkl. leerer Option „Inherited“ bzw. für Global „Configured default“). `SettingsModel` bekommt die Sprachliste über `Configure(meta, l)` (Localizer `l.Languages`).
- Server-Validierung: `Language` akzeptiert jeden String (wie `String`).
- Prüfen: Clients, die `SettingType` per `switch` auswerten (`grep -rn "SettingType\." src`), behandeln `Language` wie `String`.
- Test: `tests/Coworkee.Client.Blazor.Tests/SettingsFormTests.cs`, `UserAppearanceTests.cs` (falls Sprache dort gerendert)

- [ ] **Step 1: Failing test** `A_language_setting_is_a_select_of_the_languages`: Definition `Localization.Culture` Type `Language`; gerendertes Feld ist `MudExSelect` (Klasse/Markup prüfen wie andere Select-Tests), Items enthalten die Testsprachen der `ClientTestBase`.
- [ ] **Step 2: FAIL**
- [ ] **Step 3: Implementieren**, `de.json` ergänzen (`Default language` → „Standardsprache“, Beschreibung, „Inherited“ → „Geerbt“, „Configured default“ → „Konfigurierter Standard“).
- [ ] **Step 4: PASS**
- [ ] **Step 5: Commit** `feat(localization): default language setting is a select of the app's languages`

### Task 7: Übersetzung – Leerraum + Expand-Icon (Browser)

Durchgeführt vom Controller (Playwright gegen laufende Demo), nicht vom Subagenten. Ergebnis: Ursache + Fix + ggf. Test in der passenden Testklasse. Commit `fix(settings): …`.

### Task 8: Docs, Demo, Merge

**Files:**
- Modify: `website/docs/modules/settings.md` + `settings.de.md` (Abschnitte `enabledWhen`, `page`, `SettingType.Language`)
- Demo `C:\dev\privat\github\cleanarchitecturebaseblazor`: nach `pwsh build/pack-local.ps1` dort `dotnet restore --force-evaluate`, bauen, im Browser Admin → Settings prüfen.

- [ ] Docs schreiben, `python -m mkdocs build --strict -f website/mkdocs.yml -d <tmp>` grün.
- [ ] Voller Build + Tests grün.
- [ ] Merge `fix/settings-cleanup` → `main`, Pack, Demo aktualisieren/committen.
