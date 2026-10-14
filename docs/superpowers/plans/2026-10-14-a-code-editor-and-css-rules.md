# Part A – CodeEditor (Monaco) and CSS rules editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reusable Monaco `CodeEditor` in the lib and a `CssRulesEditor` (Rules | Raw) for the theme's `CustomCss`.

**Architecture:** `CodeEditor` wraps BlazorMonaco's `StandaloneCodeEditor`; Monaco's scripts are loaded on demand by `coworkee.js` so host apps need no script tags. `CssRulesEditor` parses the stylesheet into blocks (`CssStylesheet`), edits plain rules with `MudExStyleEditor`, keeps everything else as raw blocks, and is plugged in through `CoworkeeThemeEditMeta`.

**Tech Stack:** .NET 10, Blazor, MudBlazor 9.11.0, MudBlazor.Extensions 9.11.3, BlazorMonaco 3.5.0, bUnit, xUnit v3, Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-14-mail-campaigns-and-editors-design.md` (Part A)

## Global Constraints

- `.razor` + `.razor.cs`, small files, few comments, `// ponytail:`/`shortcut:` marks deliberate shortcuts.
- Central package versions in `Directory.Packages.props`; no attribution in commits.
- Every UI text through `CoworkeeLocalizer` (`L["…"]`) and added to the shipped languages; `pwsh build/localization-check.ps1` must pass (see how earlier keys were added under `src/Coworkee.Client.Blazor/Localization` / resources).
- Docs EN + `.de.md` with real umlauts; `python -m mkdocs build --strict -f website/mkdocs.yml -d <tmp>` passes.
- Host apps (template, demo, Sharemee) must not need new script tags.

## Review Focus

- CSS with `{`/`}` inside strings or comments (`content: "}"`, `/* a { */`) must survive a Rules round trip unchanged in meaning – test in Task 2.
- Unbalanced/garbage CSS (user mid-typing in Raw) must not throw; Rules view falls back to one raw block – test in Task 2.
- Switching Rules → Raw → Rules without edits must not reformat the CSS (no churn on save) – test in Task 2 (`Parse(x).ToString()` of an already-serialised sheet is identical).
- Empty / null `CustomCss` shows an empty Rules list with "Add rule" and stores `null`/empty, not `"\n"` – test in Task 3.
- Monaco not loadable (bUnit, offline) must not break the form: `CodeEditor` renders and the value parameter still flows – test in Task 1.

---

### Task 1: `CodeEditor` component (Monaco)

**Files:**
- Modify: `Directory.Packages.props` (add `<PackageVersion Include="BlazorMonaco" Version="3.5.0" />`), `src/Coworkee.Client.Blazor/Coworkee.Client.Blazor.csproj` (PackageReference `BlazorMonaco`)
- Modify: `src/Coworkee.Client.Blazor/wwwroot/coworkee.js` (add `export function loadMonaco()`)
- Create: `src/Coworkee.Client.Blazor/Components/Editors/CodeEditor.razor`, `CodeEditor.razor.cs`, `CodeCompletion.cs`
- Test: `tests/Coworkee.Client.Blazor.Tests/CodeEditorTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CodeCompletion(string Label, string InsertText, string? Detail = null);`
  - `CodeEditor` parameters: `string? Value`, `EventCallback<string?> ValueChanged`, `string Language = "plaintext"`, `string? Label`, `bool ReadOnly`, `string Height = "320px"`, `IReadOnlyList<CodeCompletion>? Completions`, `string? CompletionTrigger` (e.g. `"{{"`), `bool AllowFullscreen = true`.
  - `public Task InsertAsync(string text)` – inserts at the cursor (used by Part B's placeholder panel).

- [ ] **Step 1: Failing tests** in `CodeEditorTests : ClientTestBase` (JS interop is Loose):
  - `Renders_the_label_and_a_fullscreen_button` – render with `Label="CSS"`; markup contains `CSS`; `[data-testid='code-editor-fullscreen']` exists; with `AllowFullscreen=false` it does not.
  - `Without_monaco_the_value_still_flows` – render with `Value="a{}"`, call `await cut.InvokeAsync(() => cut.Instance.SetFromEditorAsync("b{}"))` (internal method the Monaco change handler calls; `InternalsVisibleTo` exists for the test project – check `Coworkee.Client.Blazor.csproj`, add if missing) → the `ValueChanged` callback received `"b{}"`.
- [ ] **Step 2:** `dotnet test tests/Coworkee.Client.Blazor.Tests -c Release --filter-class "*CodeEditorTests"` → FAIL (type missing).
- [ ] **Step 3: Implement.**
  - `loadMonaco()` in `coworkee.js`: idempotent promise that appends `<script src="_content/BlazorMonaco/jsInterop.js">`, `_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js`, then `_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js` sequentially (check the exact asset paths in the BlazorMonaco 3.5.0 package's `staticwebassets`), resolves when `window.monaco` exists.
  - `CodeEditor` awaits `loadMonaco` in `OnAfterRenderAsync(firstRender)` before rendering `<StandaloneCodeEditor>` (flag `_ready`); until then (and in bUnit) it shows a read-only `<pre>` of the value. Options: `Language`, `Value`, `ReadOnly`, `AutomaticLayout = true`, `Minimap.Enabled = false`, theme `vs-dark` when `MudThemeProvider`'s dark mode is on (use the existing dark-mode source in the lib – grep `IsDarkMode`), else `vs`.
  - Changes: `OnDidChangeModelContent` → debounce 300 ms → `SetFromEditorAsync(await editor.GetValue())` which sets `Value` and invokes `ValueChanged`. `OnParametersSet`: if `Value` differs from the editor content, `SetValue`.
  - Completions: when `Completions` is set, register once per language via `BlazorMonaco.Languages.Global.RegisterCompletionItemProvider` (trigger characters = last char of `CompletionTrigger`), items map `Label`/`InsertText`/`Detail`, kind `Variable`. Dispose the registration on dispose.
  - Fullscreen: button (icon `Icons.Material.Outlined.Fullscreen`, `data-testid="code-editor-fullscreen"`) opens a MudEx dialog (`DialogOptionsEx { FullScreen = true, CloseButton = true }`) containing another `CodeEditor` (Height `calc(100vh - 140px)`, same parameters, `AllowFullscreen=false`) bound to a local copy; on close the copy is pushed through `SetFromEditorAsync`.
  - `InsertAsync(text)`: `editor.ExecuteEdits("coworkee", [new IdentifiedSingleEditOperation { Range = selection, Text = text }])`, then focus.
- [ ] **Step 4:** run Step 2's command → PASS.
- [ ] **Step 5: Manual check** in the demo app (`C:\dev\privat\github\cleanarchitecturebaseblazor`, after `pwsh build/pack-local.ps1` and `dotnet restore --force-evaluate` there) is done in Task 3; here only build: `dotnet build Coworkee.slnx -c Release` → 0 errors.
- [ ] **Step 6: Commit** `feat(client): CodeEditor on Monaco, loaded on demand`.

### Task 2: `CssStylesheet` parser/serializer

**Files:**
- Create: `src/Coworkee.Client.Blazor/Theming/CssStylesheet.cs`
- Test: `tests/Coworkee.Client.Blazor.Tests/CssStylesheetTests.cs`

**Interfaces:**
- Produces:
  - `public abstract record CssBlock;`
  - `public sealed record CssRule(string Selector, string Declarations) : CssBlock;` – `Declarations` is the text between the braces, trimmed, in `prop: value;` form (what `MudExStyleEditor.Value` takes).
  - `public sealed record CssRawBlock(string Text) : CssBlock;` – at-rules with their whole body, comments, or unparseable rest, verbatim.
  - `public static class CssStylesheet { public static IReadOnlyList<CssBlock> Parse(string? css); public static string? Serialize(IEnumerable<CssBlock> blocks); }`

- [ ] **Step 1: Failing tests:**
  - `Plain_rules_become_rules`: `Parse("a { color: red; }\n.b{margin:0}")` → `[CssRule("a","color: red;"), CssRule(".b","margin:0")]`.
  - `At_rules_and_comments_stay_raw`: input `"/* x */\n@media (max-width: 600px) { a { color: red; } }\nb { c: d; }"` → `[CssRawBlock("/* x */"), CssRawBlock("@media (max-width: 600px) { a { color: red; } }"), CssRule("b","c: d;")]`.
  - `Braces_in_strings_and_comments_do_not_split`: `Parse("a::after { content: \"}\"; } /* { */ b { x: y; }")` → rule `a::after` with `content: "}";`, raw `/* { */`, rule `b`.
  - `Unbalanced_css_falls_back_to_one_raw_block`: `Parse("a { color: red;")` → `[CssRawBlock("a { color: red;")]`; no throw.
  - `Serialize_formats_rules_and_keeps_raw_text`: `Serialize([CssRule("a","color: red;"), CssRawBlock("@media x { b { c: d; } }")])` == `"a {\n  color: red;\n}\n\n@media x { b { c: d; } }"`.
  - `Serialized_css_round_trips_unchanged`: for that output `s`, `Serialize(Parse(s)) == s`.
  - `Empty_input`: `Parse(null)` and `Parse("  ")` → empty; `Serialize([])` → `null`.
  - Declarations with several lines serialise one declaration per line indented by two spaces (split top-level on `;`, ignoring `;` in strings/parentheses, e.g. `url(data:...;base64,...)`): `Serialize([CssRule("a","color:red;background:url(x;y)")])` == `"a {\n  color:red;\n  background:url(x;y);\n}"`.
- [ ] **Step 2:** `dotnet test tests/Coworkee.Client.Blazor.Tests -c Release --filter-class "*CssStylesheetTests"` → FAIL.
- [ ] **Step 3: Implement** a single forward scanner tracking string quote (`"`/`'` with `\` escape), comment (`/* */`), paren depth and brace depth; top level: comment → raw; `@…` → raw up to its matching `}` or `;`; otherwise selector up to `{`, body up to matching `}` → rule if the body has no nested `{`, else raw. Any unbalanced end → whole input as one raw block.
- [ ] **Step 4:** run Step 2's command → PASS.
- [ ] **Step 5: Commit** `feat(theming): css stylesheet parser for the rules editor`.

### Task 3: `CssRulesEditor` wired into the theme editor

**Files:**
- Create: `src/Coworkee.Client.Blazor/Theming/CssRulesEditor.razor`, `CssRulesEditor.razor.cs`
- Modify: `src/Coworkee.Client.Blazor/Theming/CoworkeeThemeEditMeta.cs:18-24`
- Modify: localization resources (new keys), `website/docs/…` theming page EN+DE (grep `CustomCss` under `website/docs`)
- Test: `tests/Coworkee.Client.Blazor.Tests/CssRulesEditorTests.cs`

**Interfaces:**
- Consumes: `CodeEditor` (Task 1), `CssStylesheet`/`CssRule`/`CssRawBlock` (Task 2).
- Produces: `CssRulesEditor` parameters `string? Value`, `EventCallback<string?> ValueChanged`, `string? Label`, `string? HelperText`, `bool ReadOnly`.

- [ ] **Step 1: Failing tests** (`CssRulesEditorTests : ClientTestBase`, render `MudPopoverProvider` first):
  - `Rules_mode_lists_rules_and_raw_blocks`: `Value = "a { color: red; }\n@media x { b { c: d; } }"` → one `MudExStyleEditor` and one `CodeEditor` rendered inside `[data-testid='css-rules']`; the selector input of the rule has value `a`.
  - `Adding_a_rule_emits_the_stylesheet`: empty value → `[data-testid='css-add-rule']` click → type selector `.x` → emitted value is `".x {\n}"` or contains `.x {` (exact: `Serialize([CssRule(".x","")])`, define that as `".x {\n}"` in Task 2 if not yet covered – add the assertion there too).
  - `Deleting_a_rule_removes_it`: two rules, click `[data-testid='css-delete-rule']` of the first → value equals `Serialize` of the second only.
  - `Empty_value_emits_null`: delete the only rule → `ValueChanged` got `null`.
  - `Raw_mode_shows_the_code_editor_with_the_whole_css`: click toggle `[data-testid='css-mode-raw']` → one `CodeEditor` with `Language == "css"` and `Value` equal to the input.
- [ ] **Step 2:** `dotnet test tests/Coworkee.Client.Blazor.Tests -c Release --filter-class "*CssRulesEditorTests"` → FAIL.
- [ ] **Step 3: Implement.**
  - Mode toggle (`MudToggleGroup` Rules | Raw) remembered in `localStorage` key `coworkee.cssEditorMode` via the lib's existing local storage helper (grep `localStorage`/`ILocalStorage` in `Coworkee.Client.Blazor`; if none, keep it in a static field – no new JS).
  - Rules: list of cards; `CssRule` → `MudTextField` selector (`data-testid="css-selector"`) + `MudExStyleEditor` (`Value=Declarations`, `ViewMode=DevTools`, `ShowCatalogSuggestions=true`, `Dense=true`); `CssRawBlock` → `CodeEditor Language="css" Height="120px"`; per block buttons up/down/delete (`css-delete-rule`); "Add rule" (`css-add-rule`). Every change → `CssStylesheet.Serialize` → `ValueChanged`.
  - Parse only when `Value` changes from outside (keep the block list while editing).
  - Raw: `CodeEditor Language="css"` full width, `AllowFullscreen=true`.
  - `CoworkeeThemeEditMeta`: `meta.Property(t => t.CustomCss).RenderWith<CssRulesEditor, string?>(f => f.Value, f => f.HelperText = l["Plain CSS for the whole app, applied after the theme"]).WrapInMudItem(i => i.md = 12);` – update its summary.
- [ ] **Step 4:** run Step 2's command, then the whole `tests/Coworkee.Client.Blazor.Tests` → PASS.
- [ ] **Step 5: Manual check** in the demo: pack, restore, run, Admin → Themes → edit → Custom CSS: add rule `.mud-appbar` with `background: red`, save, the app bar turns red; Raw shows Monaco with highlighting; fullscreen works; dark mode uses the dark Monaco theme. Note results in the commit body.
- [ ] **Step 6:** localization check, docs (EN+DE: theming page gets a short "Custom CSS editor" paragraph), mkdocs strict.
- [ ] **Step 7: Commit** `feat(theming): css rules editor with raw Monaco mode for custom css`.
