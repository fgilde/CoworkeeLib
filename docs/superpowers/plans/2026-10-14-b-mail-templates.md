# Part B – own mail templates, placeholders, Visual/Source editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admins create their own mail templates, see the placeholders they can use, and edit bodies in Source (Monaco with IntelliSense) or Visual (MudExRichTextEditor with `{` mentions) mode.

**Architecture:** Custom templates are a new entity `CustomMailTemplate`; their content per culture reuses `MailTemplateOverride` rows. A scoped `IMailTemplateCatalog` merges the code registry with the custom templates of the tenant and is what renderer and handlers use. Variables are derived by reflection from a template's sample model; custom templates use `MailRecipientModel`. The client editor uses Part A's `CodeEditor` and MudExRichTextEditor; mentions are converted to Scriban text so the stored body is plain Scriban.

**Tech Stack:** .NET 10, EF Core 10 (Postgres/SQL Server/SQLite), Scriban 7.5, Blazor, MudBlazor 9.11.0, MudBlazor.Extensions 9.11.3, MudExRichTextEditor 9.5.4, bUnit, xUnit v3, Shouldly, Testcontainers (module tests).

**Spec:** `docs/superpowers/specs/2026-10-14-mail-campaigns-and-editors-design.md` (Part B + "Builder's calls")

## Global Constraints

- `.razor` + `.razor.cs`, small files, few comments; `shortcut:` marks deliberate shortcuts; no attribution in commits.
- Central package versions in `Directory.Packages.props`.
- New UI texts through `CoworkeeLocalizer` in all 22 `src/Coworkee.Localization/Localization/*.json`; `pwsh build/localization-check.ps1` and `Coworkee.Localization.Tests` pass.
- EF model change → `pwsh templates/Coworkee.Templates/update-migrations.ps1`, a migration in the demo (`C:\dev\privat\github\cleanarchitecturebaseblazor`) and Sharemee (`C:\dev\privat\github\shareme`) after `pwsh build/pack-local.ps1` + `dotnet restore --force-evaluate` there; OpenAPI snapshots (demo `MYAPP_UPDATE_OPENAPI=1`, Sharemee `SHAREME_UPDATE_OPENAPI=1` + `npm run generate` in `sdk/typescript`).
- Docs EN + `.de.md`, real umlauts, mkdocs strict; never mention "ABP" or "CleanArchitectureBase".
- Custom template names: `^[A-Za-z0-9._-]+$`, immutable, unique across global and the tenant's templates and not equal to a registered name. Global ones (`TenantId = null`) only by admins of the system organisation.
- Visual mode only when the body has no Scriban block statements (`{{ if|for|while|case|capture|with|func ... }}`) and no `<table`.

## Review Focus

- A custom template saved only in `de` must render for an `en` user (fallback = first saved culture) – test in Task 2.
- Tenant A must not see, render or delete tenant B's custom template – test in Task 2.
- Deleting a custom template removes its override rows too (no orphans that resurrect when the name is reused) – test in Task 2.
- A body whose mention spans were converted must round-trip: `{{ user.first_name }}` → embed → `{{ user.first_name }}` byte-identical, and unknown `{{ foo }}` text stays text – test in Task 4.
- HTML-escaping: a mention `data-value` must only ever produce a known variable name; anything else is dropped as plain text, never injected as Scriban code – test in Task 4.

---

### Task 1: Template variables and the recipient model

**Files:**
- Create: `src/Coworkee.Mailing/MailTemplateVariables.cs`, `src/Coworkee.Mailing/MailRecipientModel.cs`
- Modify: `src/Coworkee.Contracts/Mailing/MailingContracts.cs` (add `MailVariableDto`, `MailTemplateDto.Variables`), `src/Coworkee.Mailing/MailHandlers.cs` (`GetMailTemplate` fills `Variables`)
- Test: `tests/Coworkee.Mailing.Tests/MailTemplateVariablesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MailVariableDto(string Name, string Type, string? Sample, bool IsList);` – `Name` is the Scriban path (`user.first_name`, `notifications`), `Type` one of `text|number|date|bool|list|object`.
  - `MailTemplateDto(... existing ..., IReadOnlyList<MailVariableDto> Variables)` (append as last positional parameter; update all constructions).
  - `public static class MailTemplateVariables { public static IReadOnlyList<MailVariableDto> From(object sample); }` – depth-first over properties with `StandardMemberRenamer.Default`; nested objects produce `parent.child`; `IEnumerable` (not string) produces one `IsList=true` entry plus its first item's members as `parent[].member`; `ScriptObject` samples use their keys; max depth 4.
  - `public static class MailRecipientModel { public static object For(MailRecipient recipient, string? appName, string? appUrl, string? organisationName, string? unsubscribeUrl, string? campaignName); public static object Sample { get; } }` with `public sealed record MailRecipient(Guid Id, string Email, string? FirstName, string? LastName, string? Culture);` – shape: `{ user = { first_name, last_name, display_name, email, culture }, app = { name, url }, organisation = { name }, unsubscribe_url, campaign = campaignName is null ? null : new { name } }`. `display_name` = "First Last" trimmed, else email.
- [ ] **Step 1: Failing tests:**
  - `Nested_objects_become_dotted_paths`: `From(new { user = new { first_name = "Ada" }, confirm_url = "https://x" })` → names `["user.first_name", "confirm_url"]` (order of declaration), sample of `user.first_name` = `"Ada"`, type `text`.
  - `Pascal_case_members_are_snake_cased`: `From(new { User = new { FirstName = "A" } })` → `["user.first_name"]`.
  - `Lists_expose_their_item_members`: `From(new { notifications = new[] { new { title = "T", url = "u" } } })` → entries `notifications` (`IsList`, type `list`), `notifications[].title`, `notifications[].url`.
  - `Recipient_sample_has_the_documented_paths`: `From(MailRecipientModel.Sample)` contains `user.first_name`, `user.last_name`, `user.display_name`, `user.email`, `user.culture`, `app.name`, `app.url`, `organisation.name`, `unsubscribe_url`, `campaign.name`.
  - `Display_name_falls_back_to_email`: `For(new MailRecipient(id, "a@x", null, null, null), …)` → `user.display_name == "a@x"` (read via reflection or render `{{ user.display_name }}` through `IMailTemplateRenderer.RenderContentAsync` if a renderer fixture exists in the test project).
- [ ] **Step 2:** `dotnet test tests/Coworkee.Mailing.Tests -c Release --filter-class "*MailTemplateVariablesTests"` → FAIL.
- [ ] **Step 3:** implement; `GetMailTemplate` sets `Variables = MailTemplateVariables.From(definition.SampleModel)`. Sample for `campaign.name` must be non-null in `Sample` so the variable is listed.
- [ ] **Step 4:** run Step 2 + the whole `tests/Coworkee.Mailing.Tests` → PASS (fix callers of `MailTemplateDto`, incl. client tests: `dotnet build Coworkee.slnx -c Release`).
- [ ] **Step 5: Commit** `feat(mailing): template variables and recipient model`.

### Task 2: Custom templates on the server

**Files:**
- Modify: `src/Coworkee.Mailing/MailEntities.cs` (entity `CustomMailTemplate` + EF config), `src/Coworkee.Mailing/MailTemplates.cs` (catalog), `src/Coworkee.Mailing/MailTemplateRenderer.cs`, `src/Coworkee.Mailing/MailHandlers.cs`, `src/Coworkee.Mailing/CoworkeeMailingModule.cs` (DI + endpoints), `src/Coworkee.Contracts/Mailing/MailingContracts.cs`
- Modify: template migration (script), demo + Sharemee migrations, OpenAPI snapshots + TS SDK (see Global Constraints)
- Test: `tests/Coworkee.Mailing.Tests/CustomMailTemplateTests.cs`

**Interfaces:**
- Consumes: `MailTemplateVariables`, `MailRecipientModel.Sample` (Task 1).
- Produces:
  - Entity `CustomMailTemplate { Guid Id; Guid? TenantId; string Name; string DisplayName; string? Description; DateTimeOffset CreatedAt; }`, table `cw.CustomMailTemplates`, unique index (TenantId, Name), Name max 128, DisplayName max 256.
  - `public interface IMailTemplateCatalog { Task<MailTemplateDefinition?> FindAsync(string name, CancellationToken ct); Task<IReadOnlyList<MailTemplateDefinition>> AllAsync(CancellationToken ct); bool IsCustom(MailTemplateDefinition d); }` (scoped). Custom definitions: `SampleModel = MailRecipientModel.Sample`, `Defaults` = empty dictionary. `MailTemplateContext.Add`'s `en` check stays for registered ones only.
  - Renderer/handlers use the catalog instead of `IMailTemplateDefinitionManager`. Resolution for a custom template: tenant override → global override per culture candidate, then **any** override row of that name (oldest first) as the fallback culture; if none exists, `GetMailTemplate` returns empty subject/body.
  - Contracts: `MailTemplateSummaryDto(... existing ..., bool IsCustom, string? Description)`; `public sealed record CreateMailTemplateRequest(string Name, string DisplayName, string? Description, bool Global = false, string? CopyFrom = null);`
  - Commands: `CreateMailTemplate(CreateMailTemplateRequest)` → `Result`, `DeleteMailTemplate(string Name)` → `Result` (both `[RequiresPermission(MailPermissions.Templates.Manage)]`). `CopyFrom` copies every culture's current content (override or default) of the source template into override rows of the new one.
  - Endpoints: `POST /api/v1/mail/templates` (body `CreateMailTemplateRequest`), `DELETE /api/v1/mail/templates/{name}` (route group is the existing `api` group in `CoworkeeMailingModule`).
  - `SaveMailTemplate` accepts custom names; overrides of a custom template are stored with the template's own `TenantId` (global template → null).
  - Errors: name pattern/registered/duplicate → `Error.Validation("Name", …)`; `Global` by a non-system-organisation admin → `Error.Forbidden` (use the existing check for "system organisation" – grep `IsSystem`/`SystemOrganisation` in the identity/tenancy code); delete of a registered name → validation error. (Delete-blocked-by-campaign is added in Part C.)
- [ ] **Step 1: Failing tests** (use the test project's existing module/db fixture pattern, see other tests in `tests/Coworkee.Mailing.Tests`):
  - `Created_template_is_listed_as_custom_and_renders_with_the_recipient_model`: create `News.1`, save `de` subject `Hallo {{ user.first_name }}`, render for culture `en` with `MailRecipientModel.For(...)` → subject `Hallo Ada` (fallback to the first saved culture).
  - `Names_are_validated`: `"bad name"`, `"Identity.ResetPassword"`, a duplicate → validation errors.
  - `Other_tenants_do_not_see_the_template`: create in tenant A; as tenant B `GetMailTemplates` lacks it, `GetMailTemplate` → not found, `DeleteMailTemplate` → not found.
  - `Global_needs_the_system_organisation`: `Global = true` by a tenant admin → forbidden.
  - `Delete_removes_the_content`: create, save content, delete, create again with the same name → `GetMailTemplate` returns empty subject/body.
  - `Copy_from_copies_all_cultures`: `CopyFrom = "Identity.Welcome"` → the new template has override rows for every default culture of Welcome.
- [ ] **Step 2:** `dotnet test tests/Coworkee.Mailing.Tests -c Release --filter-class "*CustomMailTemplateTests"` → FAIL.
- [ ] **Step 3:** implement as specified.
- [ ] **Step 4:** run Step 2 + whole `tests/Coworkee.Mailing.Tests` (Postgres default; also once with `COWORKEE_TEST_DATABASE=Sqlite`) → PASS.
- [ ] **Step 5:** migrations (template script, demo, Sharemee), OpenAPI snapshots + TS SDK; `dotnet build Coworkee.slnx -c Release`.
- [ ] **Step 6: Commit** lib (`feat(mailing): own mail templates`), then demo and Sharemee in their repos (`feat: migration for own mail templates`).

### Task 3: Client – list page with New / Duplicate / Delete

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Api/ICoworkeeApi.cs`, `CoworkeeApi.cs` (`CreateMailTemplateAsync(CreateMailTemplateRequest, ct)`, `DeleteMailTemplateAsync(string name, ct)`), `src/Coworkee.Client.Blazor/Pages/Admin/MailTemplates.razor(.cs)`
- Modify: localization JSONs, `website/docs` mail page EN+DE (grep `mail/templates`)
- Test: `tests/Coworkee.Client.Blazor.Tests/MailTemplatesPageTests.cs` (create if missing; else extend the existing mail page tests – grep `MailTemplates` in tests)

**Interfaces:**
- Consumes: `MailTemplateSummaryDto.IsCustom`, `CreateMailTemplateRequest` (Task 2).
- [ ] **Step 1: Failing tests:**
  - `New_template_dialog_creates_and_opens_it`: click `[data-testid='new-mail-template']`, fill name `News.1` and display name, submit → `CreateMailTemplateAsync` received `Name == "News.1"`; navigation to `/admin/mail/templates/News.1/{current culture}` (bUnit `NavigationManager`).
  - `Duplicate_passes_copy_from`: click `[data-testid='duplicate-template-Identity.Welcome']` → request with `CopyFrom == "Identity.Welcome"`, name prefilled `Identity.Welcome.copy`.
  - `Only_custom_templates_can_be_deleted`: delete button `[data-testid='delete-template-News.1']` exists for the custom row, not for `Identity.Welcome`; confirming calls `DeleteMailTemplateAsync("News.1")`.
  - The "Global" checkbox is shown only when the user is in the system organisation (use the same client-side check other admin pages use – grep `IsSystemOrganisation`/`system` in `Coworkee.Client.Blazor`).
- [ ] **Step 2:** `dotnet test tests/Coworkee.Client.Blazor.Tests -c Release --filter-class "*MailTemplatesPageTests"` → FAIL.
- [ ] **Step 3:** implement with the existing `Dialogs.ShowEditAsync` form pattern (see `Clients.razor.cs`), a small form record with `Name`, `DisplayName`, `Description`, `Global`. Custom rows show a "custom" chip.
- [ ] **Step 4:** run Step 2 + whole client test project → PASS; localization check; docs; mkdocs strict.
- [ ] **Step 5: Commit** `feat(client): create, duplicate and delete mail templates`.

### Task 4: Client – Visual/Source editor with placeholders

**Files:**
- Modify: `Directory.Packages.props` (`MudExRichTextEditor` 9.5.4 – check its MudBlazor/MudEx dependency versions don't downgrade ours), `src/Coworkee.Client.Blazor/Coworkee.Client.Blazor.csproj`
- Create: `src/Coworkee.Client.Blazor/Components/Mail/MailBodyEditor.razor(.cs)`, `MailVariablesPanel.razor(.cs)`, `MailMentionHtml.cs`, `MailVisualMode.cs`
- Modify: `src/Coworkee.Client.Blazor/Components/MailTemplateEditor.razor(.cs)` (move file into `Components/Mail/` only if no public API breaks – otherwise leave in place)
- Modify: localization JSONs, docs EN+DE
- Test: `tests/Coworkee.Client.Blazor.Tests/MailMentionHtmlTests.cs`, `MailBodyEditorTests.cs`

**Interfaces:**
- Consumes: `CodeEditor` (`Value`, `ValueChanged`, `Language`, `Completions`, `CompletionTrigger`, `InsertAsync`) and `CodeCompletion(Label, InsertText, Detail)` from Part A; `MailVariableDto` (Task 1).
- Produces:
  - `public static class MailMentionHtml { public static string ToScriban(string html, IReadOnlyCollection<string> known); public static string ToMentions(string body, IReadOnlyCollection<string> known); }` – `ToScriban` replaces each `<span class="mention" … data-value="X" …>…</span>` (the Quill mention markup; check `C:\dev\privat\github\MudExRichTextEditor\MudExRichTextEditor\wwwroot\modules` for the exact attributes) with `{{ X }}` when `X` is in `known`, otherwise with the HTML-encoded visible text; `ToMentions` replaces `{{ X }}` (whitespace-tolerant, `X` in `known`) with that span markup (`data-denotation-char="{"`, `data-id="X"`, `data-value="X"`).
  - `public static class MailVisualMode { public static bool Allowed(string body); }` – false when body matches `\{\{-?\s*(if|for|while|case|capture|with|func|tablerow)\b` or contains `<table` (case-insensitive).
  - `MailBodyEditor` parameters: `string Value`, `EventCallback<string> ValueChanged`, `IReadOnlyList<MailVariableDto> Variables`; public `Task InsertVariableAsync(MailVariableDto v)`.
  - `MailVariablesPanel` parameters: `IReadOnlyList<MailVariableDto> Variables`, `EventCallback<MailVariableDto> OnPick`.
- [ ] **Step 1: Failing tests** `MailMentionHtmlTests`:
  - `Round_trip_keeps_the_body`: body `"<p>Hi {{ user.first_name }}!</p>"`, known `["user.first_name"]` → `ToScriban(ToMentions(body)) == body`.
  - `Unknown_placeholders_stay_text`: `ToMentions("{{ foo }}", known)` == `"{{ foo }}"`.
  - `Unknown_mention_values_become_plain_text`: `ToScriban("<span class=\"mention\" data-value=\"x }}{{ evil\">x</span>", known)` contains no `{{` and contains `x`.
  - `MailVisualMode`: `Allowed("<p>{{ user.first_name }}</p>")` true; `Allowed("{{ for n in notifications }}…{{ end }}")` false; `Allowed("<TABLE>")` false.
- [ ] **Step 2: Failing tests** `MailBodyEditorTests`:
  - `Source_mode_offers_variables_as_completions`: render with `Value="{{ for x in y }}{{ end }}"` and two variables → a `CodeEditor` with `CompletionTrigger == "{{"` and `Completions` containing `CodeCompletion("user.first_name", " user.first_name }}", …)`; the Visual toggle `[data-testid='mail-mode-visual']` is disabled and the hint text "This template uses layout or logic the visual editor would change" is shown.
  - `Simple_bodies_open_in_visual_mode`: `Value="<p>Hi</p>"` → `MudExRichTextEdit` rendered.
  - `Picking_a_variable_in_source_mode_inserts_it`: switch to source, click `[data-testid='mail-variable-user.first_name']` → `CodeEditor.InsertAsync("{{ user.first_name }}")` effect: since Monaco is absent in bUnit, assert `ValueChanged` got a value containing `{{ user.first_name }}` (CodeEditor's InsertAsync fallback appends when Monaco is absent – Part A implemented a fallback; check `CodeEditor.InsertAsync`).
  - List variables insert a loop skeleton: picking `notifications` (IsList) inserts `{{ for item in notifications }}\n{{ end }}`.
- [ ] **Step 3:** run both test classes → FAIL.
- [ ] **Step 4: Implement.**
  - `MailBodyEditor`: toggle Visual | Source (`MudToggleGroup`, `data-testid="mail-mode-visual"`/`"mail-mode-source"`); initial mode Visual if `MailVisualMode.Allowed(Value)`, else Source. Source = `CodeEditor Language="html" CompletionTrigger="{{"` with completions from variables (non-list) plus Scriban keywords `if`, `else`, `end`, `for`. Visual = `MudExRichTextEdit` with `Modules=[new QuillMentionModule<MailVariableDto>((c, text) => search, '{')]` (search: contains on `Name`, max 20; use the real constructor/`ToString` from the package – `Value` shown is the variable name) bound through `MailMentionHtml.ToMentions` (in) / `ToScriban` (out). Switching Visual→Source always allowed; Source→Visual only when `Allowed`.
  - Insert in Visual mode: `MudExRichTextEdit.InsertHtmlAsync` with the mention span of `ToMentions("{{ name }}")`.
  - `MailVariablesPanel`: list (name, type, sample, `data-testid="mail-variable-{name}"`), collapsible, shown right of the editor on md+ (`MudGrid` 9/3), below on small screens. Subject field gets a small menu button that appends `{{ name }}` to the subject.
  - `MailTemplateEditor` uses `MailBodyEditor` + panel instead of the body `MudTextField`; localize the editor's existing English literals while touching it ("Preview", "Send test to me", "Reset to default", "Save", "Subject", the caption); hide "Reset to default" for custom templates (they have no default) – needs `IsCustom` on `MailTemplateDto`: add `bool IsCustom` as the last positional parameter (server sets it via `IMailTemplateCatalog.IsCustom`).
- [ ] **Step 5:** run both test classes + whole client tests + `dotnet build Coworkee.slnx -c Release` → PASS. Check that MudExRichTextEditor's static assets load in the template app without new script tags (it imports its own modules; if it needs a `<link>`/`<script>`, load it on demand in `coworkee.js` like Part A's `loadMonaco`).
- [ ] **Step 6:** localization check, docs (mail templates page: own templates, Visual/Source, placeholders, `{` mention), mkdocs strict.
- [ ] **Step 7: Commit** `feat(client): visual and source mail editor with placeholders`.
