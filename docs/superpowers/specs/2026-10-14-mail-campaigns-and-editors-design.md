# Mail campaigns, template editors and CSS editor – design

Owner request of 2026-10-14, before release 0.0.1.22 of the lib and the CLI/Studio tools.

## Intent

Admins of an app built on Coworkee want to

1. create their own mail templates (e.g. "Newsletter 123"), not only override the built-in ones,
2. send a template to many users at once (newsletter style), choosing the recipients by smart lists (presets such as "not signed in for more than N days", own queries like in reporting) or by hand (checkboxes, select all),
3. edit subjects and bodies comfortably: source mode in Monaco with IntelliSense for the template placeholders, visual mode in MudExRichTextEditor, and the available placeholders always visible on the right,
4. edit the theme's `CustomCss` either as raw CSS in Monaco (large/fullscreen) or rule by rule, each rule's declarations edited with `MudExStyleEditor`.

Three independent parts, built in this order: **A** shared Monaco editor + CSS editor, **B** template editing and own templates, **C** campaigns (bulk send). Each part is releasable alone.

## Current state (recon)

- Templates: code registry (`IMailTemplateContributor` → `MailTemplateDefinition(Name, DisplayName, SampleModel, Defaults)`), embedded Scriban defaults per culture, DB overrides `MailTemplateOverride(Name, TenantId?, Culture, Subject, Body)`. `SaveMailTemplate` rejects unknown names. Scriban with snake_case members (`{{ user.first_name }}`); body is HTML (HTML-escaped values), wrapped by the `Layout` template. No variable metadata besides the anonymous `SampleModel`. Editor: `MailTemplateEditor` with plain `MudTextField` (16 lines) + preview iframe + test mail.
- Sending: `IMailSender.QueueAsync` → `OutgoingMail` → Hangfire `SendMailJob` (queue `mail`, whitelist, retries, mail log page).
- Users: OData set `Users` (tenant filtered by `UserODataFilter`), `LastLoginAt`, `IsActive`, `EmailConfirmed`, lockout, roles. Admin page uses `CoworkeeDataTable` (OData paging, facets, advanced QueryBuilder).
- QueryBuilder produces `QueryGroup` JSON and an OData `$filter` (`ODataQueryTranslator`); `QueryDataHandler` applies a `$filter` server-side to a registered set with tenant filter and permission check (capped at 50 rows today).
- Bulk pattern to copy: `NotificationDigestJob` (distributed lock, keyset paging 500, culture per user, `IMailSender.QueueAsync` per user, save per page).
- Theme `CustomCss`: `MudTextField` in `CoworkeeThemeEditMeta`, validated server-side (20 000 chars, no `</style`, `<script`, `@import`, `expression(`, `javascript:`, …). `ThemeLogoEdit` is the pattern for a custom property editor (`Value`/`ValueChanged`/`Label`/`ReadOnly`, `RenderWith`).
- Not in the lib yet: Monaco, MudExRichTextEditor. `MudExStyleEditor` edits one declaration block (`prop: value;`), not a stylesheet. MudExRichTextEditor mentions insert a Quill embed (`<span class="mention" …>`), allow letters/spaces only, and it has no HTML source mode.

## Part A – Monaco editor and CSS editor

**`CodeEditor`** (lib component, `Coworkee.Client.Blazor/Components/Editors`), wraps BlazorMonaco 3.5.0 (new package dependency; it is the Monaco wrapper the MudEx samples already use).
- Parameters: `Value`/`ValueChanged` (string), `Language` (`html`, `css`, `scriban-html`…), `Label`, `ReadOnly`, `Height`, `Completions` (list of `CodeCompletion(Label, InsertText, Detail)` offered by IntelliSense), `AllowFullscreen`.
- Theme follows the app's dark/light mode.
- Fullscreen button opens the same editor in a MudEx dialog with `FullScreen`/`MaximizeButton`; changes flow back on close.
- Value is pushed on change (debounced) so ObjectEdit/forms see it.

**`CssRulesEditor`** for `CustomCss`, registered in `CoworkeeThemeEditMeta` via `RenderWith` (like `ThemeLogoEdit`), full width.
- Toggle **Rules | Raw**, remembered per browser.
- Raw: `CodeEditor` with `Language="css"`, fullscreen allowed.
- Rules: the CSS parsed into a list of blocks. A plain rule = selector + declarations; each shows a selector text field and a `MudExStyleEditor` for its declarations (DevTools view mode, catalog suggestions on). Add rule (empty selector, focus), delete, move up/down. Any other top-level block (`@media`, `@keyframes`, `@font-face`, comments) stays a **raw block** item edited in a small `CodeEditor`, so nothing is lost on round trip.
- Serialising writes `selector {\n  decl;\n}` per rule, blank line between blocks. Formatting of untouched raw blocks is kept verbatim.
- Server validation stays as is; the editor shows the validation error of the save.
- Parser is a small top-level brace/string/comment aware splitter with unit tests (round trip, nested `@media`, strings with braces, comments).

## Part B – template editing and own templates

**Own templates.** New entity `CustomMailTemplate(Id, TenantId?, Name, DisplayName, Description?)` plus its content per culture in the existing `MailTemplateOverride` rows (Name = the custom name). Names are free but unique per tenant and may not collide with registered names; prefix-free, validated `^[A-Za-z0-9._-]+$`.
- The template registry merges registered definitions with the custom ones of the current tenant (global ones = TenantId null, visible to all tenants).
- `en` is not mandatory for custom templates: the first culture saved is the fallback (resolution: requested culture → its parent → template's fallback culture).
- List page gets **New template** and, for custom ones, **Rename/Delete** (built-in ones keep Reset). Duplicate of any template (built-in or custom) into a new custom one.
- EF model changes → template migration script + demo/Sharemee migrations.

**Placeholders (variables).**
- `MailTemplateDefinition` gets the variable list derived from its `SampleModel` by reflection with the same snake_case renamer Scriban uses: paths like `user.first_name`, `confirm_url`, collections as `notifications[]` with their item members. Each variable has name, type hint and sample value.
- Custom templates get the **recipient model**: `user` (first_name, last_name, display_name, email, culture), `app` (name, url), `organisation` (name), `unsubscribe_url` (Part C). The same model is what campaigns render with, so preview and test mail work for custom templates too (sample = the signed-in admin).
- Contracts: `MailTemplateDto` carries `Variables`.

**Editor.** `MailTemplateEditor` gets:
- Subject: text field, plus a placeholder insert menu.
- Body: toggle **Visual | Source**.
  - Source: `CodeEditor` (`html` with Scriban), IntelliSense on `{{` offers the variables (`{{ user.first_name }}`) and Scriban keywords (`if`, `for`, `end`). Fullscreen allowed.
  - Visual: `MudExRichTextEdit` (new package dependency `MudExRichTextEditor`), value as HTML. Placeholders also by mention (owner decision): a `QuillMentionModule<MailVariable>` with trigger `{` searches the variables (contains match on the typed letters). The editor inserts a mention embed (`<span class="mention" data-value="user.first_name" …>…</span>`); the component converts embeds to `{{ user.first_name }}` when emitting the value and `{{ name }}` of known variables back to embeds when loading, so the stored body is always plain Scriban. No change to MudExRichTextEditor.
- Right side panel (collapsible, below the editor on small screens): the variables with type and sample value; clicking one inserts `{{ name }}` at the cursor in the active editor. For collections it inserts a `{{ for item in notifications }}…{{ end }}` skeleton.
- Preview and test mail as today, preview updates on demand.
- **Safety:** Quill rewrites markup it does not know (tables, Scriban blocks between tags). Visual mode is offered only when the body has no Scriban blocks (`{{ if/for/… }}`) and no `<table>`; otherwise the toggle is disabled with the hint "This template uses layout or logic the visual editor would change – edit it in source mode". Built-in templates therefore usually open in Source; new custom templates open in Visual. Switching Visual → Source is always allowed.

## Part C – campaigns (bulk send)

New admin page **Mail › Campaigns** (`/admin/mail/campaigns`), permission `Mail.Campaigns.Send` (new).

**Campaign** entity `MailCampaign(Id, TenantId?, Name, TemplateName, RecipientSource, Status (Draft/Queued/Sending/Sent/Failed/Cancelled), Total, Sent, Failed, CreatedBy, CreatedAt, SentAt)`. Each queued mail references the campaign (`OutgoingMail.CampaignId`), so the mail log filters by campaign and the campaign shows progress.

**Wizard** (one page, three steps):
1. **Template** – pick any template (custom or built-in that only needs the recipient model; built-ins needing their own model such as ResetPassword are not offered). Link "new template" opens Part B's editor.
2. **Recipients** – one of
   - **Smart list**: built-in presets and saved own lists.
     - Presets: all active users; not signed in for more than N days (N input, default 30); never signed in; signed up in the last N days; email not confirmed; members of role(s) X; inactive (deactivated) users.
     - Own lists: QueryBuilder over the `Users` fields (same component and field source as the user table's advanced filter), saved by name as `RecipientList(Id, TenantId?, Name, QueryJson, Filter)` and reusable.
   - **Manual**: the users table (`CoworkeeDataTable` on `Users`) with checkboxes, select all (all on the page / all matching the current filter), search and facets usable to narrow first.
   - Always shown: count and the first page of resolved recipients; users without e-mail, locked, deactivated (unless the preset asks for them) and opted-out users are excluded and counted separately.
3. **Send** – summary (template, culture handling, count), test mail to myself, **Send now** or **Schedule** (date/time), confirmation dialog with the count.

**Sending.** Hangfire job `MailCampaignJob` modelled on `NotificationDigestJob`: resolves recipients server-side (OData `$filter` over `Users` with tenant filter and without the 50-row cap, or the stored id list for manual selection), keyset paging 500, culture per user (user → tenant → global), `IMailSender.QueueAsync` per user with the recipient model, counters updated per page, cancellable between pages. Existing `SendMailJob` does delivery, whitelist and retries unchanged.

**Opt-out.** New user setting `Mail.Newsletters` (bool, default true, User scope). Campaigns skip users who turned it off. `unsubscribe_url` points to a signed link that turns it off without sign-in; the Layout shows it only for campaign mails.

**Tenancy.** A campaign only reaches users of the admin's own tenant (owner decision, also for the system organisation). The job runs as the sending user in that tenant, so `UserODataFilter` applies unchanged.

**Opt-out** is confirmed by the owner (setting + unsubscribe link).

## Builder's calls (made by the designer, owner may override)

- **Recipients are resolved at send time.** The campaign stores a copy of the source: `RecipientSource` JSON = `{ kind: preset|query|manual, preset args | QueryJson + Filter | user ids, roleIds? }`. A saved `RecipientList` is copied in, not referenced, so editing the list later does not change a scheduled campaign. Step 2's count is a preview; `Total` is set when the job starts.
- **Campaign marker and unsubscribe link.** The recipient model carries `campaign` (`{ name }`, null outside campaigns); the Layout shows the unsubscribe line only `{{ if campaign }}`. `unsubscribe_url` = anonymous route `/api/v1/mail/unsubscribe?token=…`, token = Data Protection protector (purpose `Coworkee.Mail.Unsubscribe`) over user id + tenant id, no expiry (Data Protection keeps retired keys for unprotect). The route sets `Mail.Newsletters=false` for that user and shows a small confirmation page.
- **Template names are fixed.** Custom templates have an immutable `Name` (the key) and an editable `DisplayName`; there is no rename. Delete is refused while a Draft/Queued campaign uses the template. Campaigns keep `TemplateName` as a string snapshot.
- **Campaigns send custom templates only.** Built-in templates need their own models; none is offered. (A flag on `MailTemplateDefinition` can open this later.)
- **Global custom templates and lists** (`TenantId = null`) are created by admins of the system organisation only. A name must be unique across global and the tenant's templates (no shadowing).
- **Role preset** resolves server-side through the identity role store (user ids of the roles), not through OData; the recipient source carries `roleIds`.
- **Counters and cancel.** `Total` = recipients queued by the job. `Sent`/`Failed` are counted from `OutgoingMail` rows by `CampaignId` when the campaign is read; `SendMailJob` is unchanged. Status `Sent` when the job queued everyone, `Failed` only if the job itself crashed. Cancel stops queuing further pages and deletes the campaign's `OutgoingMail` rows still in `Queued`.

## Non-goals

- Open/click tracking, A/B tests, bounce handling, external newsletter services.
- Plain-text mail bodies (all mails stay HTML).
- Sending to users of other tenants (owner: own tenant only, also for the system organisation).
- Editing the `Layout` template visually.

## Anti-goals

- Rich text mode silently breaking existing templates.
- A campaign reaching users of another tenant, or users who opted out.
- Bulk send blocking the request or the UI; it is always a background job.

## Constraints

- Owner rules: `.razor` + `.razor.cs`, small files, few comments, docs EN + `.de.md`, all UI texts localised in the shipped languages (`build/localization-check.ps1`).
- EF changes: template migration script, demo + Sharemee migrations, OpenAPI snapshots + TS SDK.
- New packages: BlazorMonaco, MudExRichTextEditor (central versions in `Directory.Packages.props`; static assets must work in the template app and Sharemee).

## Left to the builder

Exact component/file names, layout details of the wizard, debounce timings, how the fullscreen dialog syncs, wording of hints.
