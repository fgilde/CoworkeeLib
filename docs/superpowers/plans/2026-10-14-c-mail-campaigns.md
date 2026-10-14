# Part C – mail campaigns (bulk send) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admins send a custom mail template to many users of their tenant, chosen by presets, saved queries or by hand, now or scheduled, with opt-out and unsubscribe link.

**Architecture:** Campaign entities, the job and endpoints live in `Coworkee.Mailing`. Mailing cannot see the `User` entity, so it defines `IMailRecipientDirectory`; `Coworkee.Account` (references Identity + Mailing) implements it with the OData `Users` set (tenant filter via `ODataEntityAccess.VisibleAsync`) and the role store. `MailCampaignJob` (Hangfire through `IBackgroundJobs`, runs as the sending user in the tenant) pages recipients and queues one `OutgoingMail` per user with `CampaignId`; delivery stays in `SendMailJob`.

**Tech Stack:** .NET 10, EF Core 10, Hangfire 1.8 (`Coworkee.BackgroundJobs`), ASP.NET Core OData, Data Protection, Blazor/MudBlazor/MudEx, bUnit, xUnit v3, Shouldly, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-14-mail-campaigns-and-editors-design.md` (Part C + "Builder's calls")

## Global Constraints

- Campaigns reach only users of the sender's own tenant (also for the system organisation); the job runs as the sending user in that tenant so `UserODataFilter` applies.
- Only custom templates can be sent (`IMailTemplateCatalog.IsCustom`).
- Excluded always: users without e-mail, locked out (`LockoutEnd > now`), opted out (`Mail.Newsletters` = false); deactivated (`IsActive = false`) unless the preset is `Inactive`. Excluded users are counted separately in the preview.
- Recipients are resolved at send time from a copy stored on the campaign (`RecipientSource` JSON); a saved `RecipientList` is copied in, never referenced.
- `Mail.Newsletters`: bool, default true, User scope, visible to the user in their settings. Unsubscribe link: anonymous `GET /api/v1/mail/unsubscribe?token=…`, Data Protection purpose `Coworkee.Mail.Unsubscribe`, payload user id + tenant id, no expiry; sets the setting to false; answers a small localized HTML page.
- Permission `Mail.Campaigns.Send` (group `Mail`).
- `.razor` + `.razor.cs`, small files, few comments, `shortcut:` marks; no attribution in commits; UI texts in all 22 languages; docs EN + `.de.md`; mkdocs strict; never "ABP"/"CleanArchitectureBase".
- EF changes: template migration script, demo + Sharemee migrations, OpenAPI snapshots + TS SDK (see `CONTINUE.md` "How to build, test, release").

## Review Focus

- Tenant isolation: a campaign created in tenant A, run by the job, must never queue mail to a tenant B user, even with a manual id list containing B's ids – test in Task 2.
- Opted-out user present in a manual id list is skipped – test in Task 2.
- A tampered/foreign unsubscribe token returns 400 and changes nothing – test in Task 3.
- Cancel while the job runs: no further pages are queued and still-`Queued` mails of the campaign are deleted, already `Sent` ones stay – test in Task 3.
- An invalid OData filter in a saved query yields a validation error in preview and marks a scheduled campaign `Failed` instead of crashing the worker loop – test in Task 2 (preview) and Task 3 (job).

---

### Task 1: Domain, contracts, opt-out setting

**Files:**
- Modify: `src/Coworkee.Mailing/MailEntities.cs` (entities + EF config, `OutgoingMail.CampaignId` + index), `src/Coworkee.Mailing/MailDelivery.cs` (`IMailSender` overload), `src/Coworkee.Mailing/CoworkeeMailingModule.cs` (permission, setting definition), `src/Coworkee.Contracts/Mailing/MailingContracts.cs`
- Create: `src/Coworkee.Contracts/Mailing/MailCampaignContracts.cs`
- Test: `tests/Coworkee.Mailing.Tests/MailCampaignDomainTests.cs`

**Interfaces:**
- Produces:
  - `MailPermissions.Campaigns.Send = "Mail.Campaigns.Send"` (registered with display "Send mail campaigns").
  - Setting `MailSettings.Newsletters = "Mail.Newsletters"` (bool, default `"true"`, scopes User (+ Global so an operator can default it), visible to client; display "Receive newsletters"), defined where the module's other settings are defined.
  - Entities:
    - `MailCampaign : AggregateRoot { Guid Id; Guid? TenantId; string Name (max 256); string TemplateName (max 128); string RecipientSource (json); MailCampaignStatus Status; DateTimeOffset? ScheduledFor; int Total; Guid CreatedBy; DateTimeOffset CreatedAt; DateTimeOffset? StartedAt; DateTimeOffset? FinishedAt; string? Error; }` table `cw.MailCampaigns`.
    - `RecipientList { Guid Id; Guid? TenantId; string Name (max 256); string Source (json); DateTimeOffset CreatedAt; }` table `cw.MailRecipientLists`, unique (TenantId, Name).
    - `OutgoingMail.CampaignId` (`Guid?`, indexed); `OutgoingMail.Queue(...)` gets an optional `Guid? campaignId = null`.
  - `IMailSender`: add `Task<Guid> QueueAsync(string to, string template, object model, string? culture, Guid? campaignId, CancellationToken cancellationToken);` and turn the existing 5-parameter member into a default interface method calling it with `null`.
  - Contracts (`MailCampaignContracts.cs`):
    - `enum MailCampaignStatus { Draft, Scheduled, Sending, Sent, Failed, Cancelled }` (string enum converter like `OutgoingMailStatus`).
    - `enum RecipientPreset { AllActive, NotSignedInSince, NeverSignedIn, SignedUpWithin, EmailNotConfirmed, InRoles, Inactive }`.
    - `record RecipientSourceDto(RecipientSourceKind Kind, RecipientPreset? Preset = null, int? Days = null, IReadOnlyList<Guid>? RoleIds = null, string? Filter = null, string? QueryJson = null, IReadOnlyList<Guid>? UserIds = null);` with `enum RecipientSourceKind { Preset, Query, Manual }`.
    - `record RecipientPreviewDto(int Count, int Excluded, IReadOnlyList<RecipientRowDto> First);` `record RecipientRowDto(Guid Id, string Email, string? DisplayName, DateTimeOffset? LastLoginAt);`
    - `record MailCampaignDto(Guid Id, string Name, string TemplateName, RecipientSourceDto Recipients, MailCampaignStatus Status, DateTimeOffset? ScheduledFor, int Total, int Sent, int Failed, int Queued, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Error);`
    - `record SaveMailCampaignRequest(string Name, string TemplateName, RecipientSourceDto Recipients);` `record ScheduleMailCampaignRequest(DateTimeOffset? At);` (null = now)
    - `record RecipientListDto(Guid Id, string Name, RecipientSourceDto Source);` `record SaveRecipientListRequest(string Name, RecipientSourceDto Source);`
- [ ] **Step 1: Failing tests:** `Queue_with_campaign_sets_the_campaign_id` (via `IMailSender` against the test db: queue with a campaign id → stored `OutgoingMail.CampaignId` equals it); `Newsletter_setting_is_defined_with_default_true` (setting definition lookup through the settings definition manager); `Campaign_permission_is_registered`.
- [ ] **Step 2:** `dotnet test tests/Coworkee.Mailing.Tests -c Release --filter-class "*MailCampaignDomainTests"` → FAIL.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** run Step 2 + whole Mailing tests; `dotnet build Coworkee.slnx -c Release`.
- [ ] **Step 5: Commit** `feat(mailing): campaign domain, contracts and newsletter opt-out setting` (migrations come in Task 3, once the model is final).

### Task 2: Recipient resolution

**Files:**
- Create: `src/Coworkee.Mailing/Campaigns/IMailRecipientDirectory.cs`, `src/Coworkee.Account/MailRecipientDirectory.cs` (+ DI registration in the Account module), `src/Coworkee.Mailing/Campaigns/RecipientHandlers.cs`
- Modify: `src/Coworkee.OData` only if a public helper is needed to apply a `$filter` string to an `IQueryable<T>` (extract from `QueryDataHandler.QueryAsync` into a public `ODataFilter.ApplyAsync<T>(IServiceProvider, string? filter, CancellationToken)` returning the visible, filtered query; `QueryDataHandler` then uses it)
- Modify: `CoworkeeMailingModule.cs` endpoints
- Test: `tests/Coworkee.Account.Tests` or the test project that hosts Account integration tests (grep `Account` in tests) – `MailRecipientDirectoryTests.cs`

**Interfaces:**
- Consumes: `MailRecipient` (Part B Task 1), contracts from Task 1.
- Produces:
  - `public interface IMailRecipientDirectory { Task<RecipientPage> PageAsync(RecipientSourceDto source, Guid? after, int size, CancellationToken ct); Task<RecipientPreviewDto> PreviewAsync(RecipientSourceDto source, int first, CancellationToken ct); }` and `public sealed record RecipientPage(IReadOnlyList<MailRecipient> Recipients, Guid? Next);` – keyset paging by user id, only eligible users (Global Constraints), within the current tenant.
  - Preset semantics (`now` from `TimeProvider`): `AllActive` all eligible; `NotSignedInSince` `LastLoginAt < now - Days` or null with `CreatedAt < now - Days`; `NeverSignedIn` `LastLoginAt == null`; `SignedUpWithin` `CreatedAt >= now - Days`; `EmailNotConfirmed` `!EmailConfirmed`; `InRoles` members of any `RoleIds`; `Inactive` `IsActive == false` (the only preset that includes deactivated users). `Days` required (>0) for the three day presets → else validation error.
  - `Query` applies `Filter` (OData `$filter` over `Users`) through the OData helper; invalid filter → `Error.Validation("Filter", message)`.
  - `Manual` = `UserIds` intersected with the tenant's eligible users.
  - Culture of each recipient: own `Localization.Language` user setting (grep the setting name used by `NotificationDigestJob.CulturesAsync`), else tenant, else global; reuse that logic by moving it into a shared helper if it is private (keep behaviour).
  - Queries/endpoints (permission `Mail.Campaigns.Send`): `PreviewRecipients(RecipientSourceDto)` → `POST /api/v1/mail/recipients/preview` (first 20); recipient lists CRUD: `GET/POST /api/v1/mail/recipient-lists`, `PUT/DELETE /api/v1/mail/recipient-lists/{id}` (names unique per tenant).
- [ ] **Step 1: Failing tests** (seed users in two tenants via the existing identity test helpers):
  - `Presets_select_the_documented_users` – one `[Theory]` row per preset with seeded users (e.g. last login 40 days ago vs 5 days, `Days = 30`).
  - `Locked_unconfirmed_mailless_and_opted_out_users_are_excluded_and_counted` – preview `Excluded` equals the number of such seeded users for `AllActive`.
  - `Manual_ids_of_another_tenant_are_ignored`.
  - `Invalid_filter_is_a_validation_error` – `Filter = "nope eq"`.
  - `Query_filter_selects_users` – `Filter = "startswith(Email,'a')"`.
  - `Paging_returns_every_user_once` – 7 users, size 3 → 3 pages, no duplicates.
  - `Recipient_lists_are_per_tenant`.
- [ ] **Step 2:** run the new test class → FAIL.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** run new tests + Mailing + Account + OData test projects → PASS.
- [ ] **Step 5: Commit** `feat(mailing): campaign recipients by preset, query or hand`.

### Task 3: Campaigns – endpoints, job, unsubscribe, migrations

**Files:**
- Create: `src/Coworkee.Mailing/Campaigns/MailCampaignHandlers.cs`, `MailCampaignJob.cs`, `Unsubscribe.cs`
- Modify: `CoworkeeMailingModule.cs` (endpoints, job registration), `src/Coworkee.Mailing/Campaigns/...`, `src/Coworkee.Mailing/MailTemplateRenderer.cs` (layout gets `unsubscribe_url`), the 22+ embedded `Templates/Layout.*.body.scriban` files (footer line), `CustomMailTemplateHandlers` (delete blocked by Draft/Scheduled/Sending campaigns), `src/Coworkee.Contracts/Mailing/MailingContracts.cs` (`OutgoingMailDto` + `CampaignId`; `GetOutgoingMails` gets optional `Guid? CampaignId` filter)
- Migrations: template script, demo, Sharemee; OpenAPI snapshots + TS SDK (also covers Part B's `MailTemplateDto.IsCustom`)
- Test: `tests/Coworkee.Mailing.Tests/MailCampaignTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts/entities, Task 2 `IMailRecipientDirectory`, `MailRecipientModel.For` (Part B), `IMailTemplateCatalog`.
- Produces endpoints under the mail api group (permission `Mail.Campaigns.Send`):
  - `GET /campaigns` → `IReadOnlyList<MailCampaignDto>` (newest first); `GET /campaigns/{id}`; `POST /campaigns` (`SaveMailCampaignRequest`, creates Draft) → `MailCampaignDto`; `PUT /campaigns/{id}` (Draft only); `DELETE /campaigns/{id}` (Draft/Sent/Failed/Cancelled only);
  - `POST /campaigns/{id}/test` → queues the campaign mail to the current user (rendered with the current user as recipient);
  - `POST /campaigns/{id}/schedule` (`ScheduleMailCampaignRequest`): Draft → Scheduled; `At` null or past → enqueue now, else `IBackgroundJobs.Schedule` at `At`;
  - `POST /campaigns/{id}/cancel`: Scheduled/Sending → Cancelled, deletes the campaign's `OutgoingMail` rows in `Queued`.
  - `Sent`/`Failed`/`Queued` in the DTO are counted from `OutgoingMail` by `CampaignId` (one grouped query for the list).
- `MailCampaignJob` (args: campaign id): skips unless status Scheduled; sets Sending/StartedAt; pages `IMailRecipientDirectory.PageAsync(source, after, 500)`; before each page reloads the status and stops if Cancelled; per recipient `IMailSender.QueueAsync(email, template, MailRecipientModel.For(recipient, appName, appUrl, organisationName, unsubscribeUrl, campaign.Name), culture, campaign.Id, ct)`; per-recipient failures are logged and skipped like `NotificationDigestJob`; `SaveChanges` per page; `Total` += queued; end → Sent/FinishedAt; exception → Failed + `Error` (message, max 2000). App name/url: the same sources the lib already uses for links and the app title (`NotificationOptions.PublicAppUrl`; app name – grep how the Layout/app bar gets it, else `KeycloakThemeOptions.AppName`, else empty). Organisation name: the tenant's name (grep the tenant entity).
- Unsubscribe: `IMailUnsubscribeLinks.Create(Guid userId, Guid? tenantId)` → absolute URL from `PublicAppUrl` (or the request base when called in a request); `GET /api/v1/mail/unsubscribe?token=` `AllowAnonymous`, writes `Mail.Newsletters=false` at User scope for that user through the settings module's writer (grep how `SettingHandlers` saves a user value), returns `text/html` page "You will no longer receive newsletters." (localized by the user's culture).
- Layout: renderer passes `unsubscribe_url` from the model (if the model has that property and it is non-empty) into the layout model; each `Layout.*.body.scriban` gets `{{ if unsubscribe_url }}<p style="font-size:12px;color:#888"><a href="{{ unsubscribe_url }}">…localized "Unsubscribe from newsletters"…</a></p>{{ end }}` above the closing content wrapper.
- [ ] **Step 1: Failing tests:**
  - `Sending_queues_one_mail_per_eligible_recipient_with_the_campaign_id` (3 eligible + 1 opted out + 1 other tenant → 3 `OutgoingMail` with `CampaignId`, `Total = 3`, status Sent; run the job directly via DI, not through Hangfire).
  - `Mails_contain_the_unsubscribe_link_and_it_opts_out` – rendered HTML contains `/api/v1/mail/unsubscribe?token=`; GET of that link (anonymous client) → 200 and the user's `Mail.Newsletters` is `false`; a second campaign skips the user.
  - `Tampered_token_is_rejected` → 400, no setting written.
  - `Only_custom_templates_can_be_sent` → create with `Identity.Welcome` → validation error.
  - `Cancel_stops_and_removes_queued_mails` – job with page size forced small (internal constant overridable for tests, or seed > 500? use an internal settable `PageSize` for tests) and cancel between pages; already queued and still `Queued` rows are deleted.
  - `Invalid_filter_marks_the_campaign_failed`.
  - `Template_in_use_cannot_be_deleted` – Draft campaign uses `News.1` → `DeleteMailTemplate` → validation error.
  - `Counts_come_from_the_mail_log` – mark one mail Sent, one Failed → DTO `Sent = 1, Failed = 1, Queued = 1`.
  - `Outgoing_mails_filter_by_campaign`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** new tests + Mailing + Account tests (Postgres, then once Sqlite) → PASS.
- [ ] **Step 5:** migrations (template script, demo, Sharemee), OpenAPI snapshots + TS SDK in demo and Sharemee; `dotnet build Coworkee.slnx -c Release`.
- [ ] **Step 6: Commit** lib `feat(mailing): mail campaigns with scheduled sending and unsubscribe`; demo/Sharemee `feat: migration for mail campaigns` (no push).

### Task 4: Client – campaigns page and wizard

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Api/ICoworkeeApi.cs`, `CoworkeeApi.cs` (one method per endpoint of Tasks 2–3)
- Create: `src/Coworkee.Client.Blazor/Pages/Admin/MailCampaigns.razor(.cs)` (`/admin/mail/campaigns`), `src/Coworkee.Client.Blazor/Components/Mail/CampaignWizard.razor(.cs)`, `RecipientPicker.razor(.cs)`
- Modify: admin navigation (where the other mail pages are registered – `CoworkeeClientExtensions.cs` around the mail entries), `MailLog.razor(.cs)` (campaign filter via query string `?campaign={id}`)
- Modify: localization JSONs, `website/docs/modules/mailing.md` + `.de.md` (campaigns, opt-out, unsubscribe)
- Test: `tests/Coworkee.Client.Blazor.Tests/MailCampaignsPageTests.cs`

**Interfaces:**
- Consumes: all contracts of Tasks 1–3; `QueryBuilder` + field source used by `CoworkeeDataTable`'s advanced filter (`DataTableQueryFields`, `ODataQueryTranslator.ToFilter`, `QueryJson`); `CoworkeeDataTable` for the manual user table (`EntitySet="Users"`, `UserRow`).
- [ ] **Step 1: Failing tests:**
  - `Page_lists_campaigns_with_progress` – rows show name, template, status chip, `Sent/Total`; a link `[data-testid='campaign-log-{id}']` to `/admin/mail/log?campaign={id}`.
  - `Wizard_offers_only_custom_templates` – template select items = custom summaries only.
  - `Preset_with_days_previews_count` – choose `NotSignedInSince`, days 30 → `PreviewRecipientsAsync` received `Preset == NotSignedInSince && Days == 30`; the count text shows `Count` and `Excluded`.
  - `Query_source_sends_the_odata_filter_and_can_be_saved_as_list` – build one rule in the QueryBuilder (Email contains `a`) → preview request `Kind == Query` with non-empty `Filter` and `QueryJson`; "Save as list" calls `SaveRecipientListAsync`.
  - `Manual_selection_sends_the_checked_ids` – check two rows → `Kind == Manual`, `UserIds` = those two; "select all matching" switches to `Kind == Query` with the table's current filter.
  - `Send_now_confirms_with_the_count` – confirm dialog text contains the count; confirming calls `SaveMailCampaignAsync` then `ScheduleMailCampaignAsync(id, null)`.
  - `Schedule_sends_the_chosen_time`.
  - `Cancel_is_offered_for_scheduled_and_sending_campaigns`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement: page = list + "New campaign" opening `CampaignWizard` in a large MudEx dialog (`MudStepper` or three sections: Template, Recipients, Send); `RecipientPicker` = tabs Smart lists (presets with Days/Roles inputs, saved lists, QueryBuilder with "Save as list") / Manual (`CoworkeeDataTable` with `MultiSelection`, "select all matching"); preview box (count, excluded, first rows); Send step (summary, "Send test to me", Send now / Schedule with `MudDatePicker`+`MudTimePicker`). Polling: the list refreshes every 5 s while a campaign is Sending (dispose timer).
- [ ] **Step 4:** run new tests + whole client tests; localization check; docs; mkdocs strict.
- [ ] **Step 5: Commit** `feat(client): mail campaigns page with recipient picker and scheduling`.
