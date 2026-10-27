# Changelog

Changes that need action in apps built on Coworkee. Newest first.

## Unreleased

### Added

- OData: `Coworkee:OData:CaseInsensitive` (`CoworkeeODataOptions.CaseInsensitive`, default `true`) sets whether names in OData URIs match regardless of case (`$filter=name eq 'x'`); it was always on. See [OData](../web/odata.md).
- OData: `AddODataEntity<T>(..., o => o.Key(c => c.IsoCode))` registers a set keyed by another property of any primitive type; by-key reads are `/odata/Countries('DE')`. Record attachments, social, workflows, entity history and webhook entity events refuse entities without a Guid `Id` at registration; client entities skip such sets. See [OData](../web/odata.md#keys-other-than-id).
- OData: computed properties (`ODataComputedProperty`) can be selected (`$select=ReferenceNumberDisplay`) and then come back with their value; an expression over the row's own columns works on sets of external databases too. See [OData](../web/odata.md#computed-properties).

### Changed

- `CodeCompletion.InsertText` is the whole text now, trigger included (`{{ name }}` instead of ` name }}`): a pick replaces what was typed of `CompletionTrigger` and the closing brackets Monaco added, and Ctrl+Space inserts a complete text anywhere. New `Snippet` flag for Monaco snippets (`{{ if $1 }}`). Own completions passed to `CodeEditor` need the trigger in their text.
- Mail body editor: bodies whose markup the visual editor would change (styles other than `color`, `background-color` and `text-align`, tags beyond text formatting, comments) open in Source, so a styled button is no longer lost on save. The shipped templates with a button open in Source.

### Added

- The daily notification digest leaves out notifications that went out as a mail of their own.
- Notifications: types with categories and per-user preferences (`AddNotificationTypes`, scope Off/All/Followed, in the app and by e-mail, `cw.NotificationPreferences`), following entities (`Followable<T>`, `cw.NotificationFollows`, `CoworkeeFollowButton`), `INotifier.PublishAsync(NotificationMessage)` for recipients, followers and an audience, e-mails per notification (template `Notifications.Notification`) with delay and debounce (`cw.PendingNotificationMails`, job `coworkee-notification-mails`), done state, counts per category and search in the inbox (tabs *Inbox*, *Preferences*, *Following* on `/notifications`), new `ICoworkeeApi` members. Apps need a migration. See [realtime](../modules/realtime.md#preferences-following-and-delayed-mails).
- Comments: `Status` (`None`, `Open`, `Closed`) and app `Properties`, `PUT /api/v1/comments/{id}/status` with the new permission `Social.Comments.SetStatus`, `SocialOptions.ValidateComments` (a check before every change), the domain event `CommentChanged` (after the commit). `CoworkeeComments` shows the status, offers a status menu and takes `NewStatus` for new comments; `ISocialApi.SetCommentStatusAsync` and `AddCommentAsync(..., CommentRequest)`. `Comment` is an aggregate root now; apps need a migration. See [social](../modules/social.md#status-app-values-and-checks).
- Exports: `GET /api/v1/data/{set}/export/stream` streams progress lines (NDJSON) and ends with a one-time anonymous download link (five minutes); a client that leaves gets the file by mail. `POST /api/v1/data/{set}/export/mail` mails an export to the user and further recipients. Scheduled exports (`cw.ExportSchedules`, permission `DataTransfer.ExportSchedules`, job `coworkee-export-schedules`): *Schedule export* in the table's export menu, page *Scheduled exports*, `IDataTransferApi` members, mail template `Data.Export`, `IExportMailer`. `ReportSchedule` has `Every` and `Week` (monthly on the last Friday), also for report subscriptions (new columns). Apps need a migration. See [import and export](../web/import-export.md#progress-stream-and-one-time-download).
- Temporary file links: `POST /api/v1/files/{id}/token` and `POST /api/v1/attachments/{type}/{id}/{slot}/{attachmentId}/token` return a token that opens the file without sign-in for five minutes (`/api/v1/files/token/{token}/{name}`, inline or `?download=true`); *Copy temporary link* on the *Files* page, `IFilesApi.CreateTokenAsync`, `IAttachmentsApi.CreateTokenAsync`. `IAccessTokens` (table `cw.AccessTokens`, single use or until expiry, only hashes stored) for own anonymous links. Apps need a migration. See [files](../modules/files.md#temporary-links).
- `CodeEditor.Highlight`: a regular expression whose matches are highlighted; the mail body editor highlights `{{ ... }}`.
- Mail body editor: placeholders are chips showing their name, the visual toolbar has the mail formatting tools and *Insert placeholder*, Ctrl+Space opens the placeholder list in both modes, `{{` works like `{` in Visual. See [mailing](../modules/mailing.md).

### Fixed

- The visual mail editor showed an empty toolbar: `RichTextEditor` passed `Tools="null"`, which MudExRichTextEditor 9.5.5 reads as no tools. It passes `Tools` only when set now.
- Source mode of the mail editor: typing `{{` and picking a placeholder wrote `{{ name }}}}`, because Monaco had closed the braces already.
- Security, OData: row filters and set permissions now hold for navigations too. `$expand` shows only the rows the target set lists for the user (a hidden single navigation is `null`), `$filter` with `any`/`all`/`$count` and `$orderby` through a navigation see only visible rows, also in exports, `query_data`, facet filters, `ODataEntityAccess.FilteredAsync` and the global search. `$expand` into a set without its permission answers `403`; into a type whose `IODataEntityFilter<T>` does more than `Where` (`OrderBy`, `Take`, a new query) `400`. Make such filters plain `Where` calls if their type is reached through navigations. `ODataNavigationScope` applies the rules to own queries. See [OData](../web/odata.md#navigations-follow-their-target).
- Security, reports: fields through a navigation (`Customer/Name`) group and aggregate only over the rows the target set shows the user; a hidden target counts as empty. Before, only the set's permission applied. Table widgets and subscription sheets read them the same way.
- OData: `Hide` and `HideUnless` work for members a base class declares, also when the EDM shares the base type between sets (a navigation points to the base): the member stays in the model and is refused on the hiding set only (`403`), with a warning at startup. Protected properties are checked by the row's own type, so a `HideUnless` on an inherited property holds in lists and expanded rows. See [OData](../web/odata.md#hiding-properties).
- OData `400` answers carry OData's own message in `detail`; other query errors (EF cannot translate the query, which can quote row filters) carry none and are logged.
- Security, OData: the checks of protected properties, `$expand` permissions and navigation filters read the options as OData parses them; a key with a space (`?+$filter=`, `%20$expand=`) slipped past all of them. Parameter aliases (`@a=Size`) and `$apply` through navigations answer `400`. `[AutoExpand]` navigations follow their target (a hidden or forbidden target is `null` or empty).
- Security, OData: facets count on the narrowed rows (a `$filter` through a navigation counted hidden rows), and a facet label read through a navigation (`LabelPath = "it.Customer.Name"`) shows the key for rows the user may not see.
- Security, OData: computed properties with a `Permission` are refused behind navigations (`$expand=Bins($select=Volume)`, `$filter=Featured/Volume gt 0`) and left out of expanded rows; `Hide` on a navigation of a shared base holds on rows reached through the base.
- OData: two filtered reference levels (`Invoice/Shipment/Customer`) and a collection under a hidden reference translate; a row filter that fails on the in-memory query the scope reads it from hides only its own type, and only the navigation targets a query reaches are checked. `ODataNavigationScope.For(services, type)` with `ApplyAsync` replaces `ForAsync` and `Apply`; reports check each target once per query.
- OData: `/odata/<Set>/$count` answered `500`; the model's page size (`[Page]`) applies on entity sets again.
- Global search: fields through a navigation (`t => t.Bin!.Name`) are read in the query with the navigation's rules; a hidden target leaves the field empty. Before, the title read them from the loaded row, where navigations are not loaded.
- Reports on SQLite group `DateTimeOffset` fields of a Coworkee DbContext by date (year, quarter, month, week, day, in UTC); they were rejected. The DbContext stores them as numbers; the buckets are computed from those.
- Security, OData: the checks of protected, hidden and computed properties run on the bound query instead of its text: `Featured/ Size` (a space after `/`) and `$compute=Owner/ Code as C` slipped past them, and literals naming a property (`'a/Note'`) were refused. Export, report filters, facet filters and `query_data` use the same check. `$search` (lists and exports) matches only properties the user may read. Facets whose value is read through a navigation are left out. `$apply` through navigations is refused only into rows with row filters or permissions.
- Security, OData: entity-set aggregates in `$apply` (`aggregate(Employees(Salary with max as M))`) read protected properties past the checks; every member a parsed node holds is checked now, and a query the checks cannot parse answers `400`. `ISearchable` search fields through a navigation follow the target's rules. Facets through a navigation are left out only when the target narrows or the value is denied; keys and targets without rules stay. A member of a shared base hidden on one set stays readable on the others.
- Security, OData: `ISearchable` search fields are checked on every member they read (concatenations, method calls), and left out through navigations into filtered targets; a facet read by the target's key needs the row's own mapped, readable foreign key to EF's key, else it follows the target's rules; facets through a collection are refused at startup; a query node the checks cannot read answers `400`. Reports on SQLite refuse date buckets of an external context's `DateTimeOffset` cleanly.
- `AddODataImport` with an id (update, upsert) refuses a set keyed by something other than a Guid at registration, and updates rows of sets keyed by a Guid other than `Id`. `AddExtendedAttributes` refuses an entity without a Guid `Id`.
- Security, OData: query options without `$` (`filter=`, `orderby=`) are no longer read; they slipped past the checks of protected properties. Clients send `$filter`.

### Security

- Anonymous token links (temporary file and attachment links, export downloads) are rate limited per address (policy `tokens`, 120 per minute), and their tokens are masked (`***`) in the logs of ASP.NET Core, HttpClient and YARP (scopes included) and in the activities of incoming, forwarded and outgoing requests, in the API and the BFF; `Coworkee.Core.Diagnostics.SecretPaths` for own hosts and logs. Coworkee.Bff now references Coworkee.Core. Expired tokens go with the retention target `access-tokens`.
- Exports: streamed exports have a per-user limit (`Coworkee:OData:MaxConcurrentExports`, 429), hand exports above `BackgroundExportRows` to the export job and time out (`Coworkee:OData:ExportTimeout`); stale running exports fail. Scheduled exports send nothing once the owner is deactivated or lost `DataTransfer.ExportSchedules` or the set's permission, and exports of selected groups fail when none of them is accessible instead of listing all.
- Notifications reach followers only while they still see the entity (checked as each follower against the `Followable<TEntity>` set); followers of `Followable(string)` types only through an `Audience`. Waiting notification mails are written through the outbox after the sending command commits, are unique per user, type and entity, are claimed by version and never fail the command. The job `coworkee-stale-exports` fails exports stuck past `ExportTimeout`; without the Retention module expired access tokens are deleted when another token is redeemed.
- Comments: changing a status, also by the author or when reopening, needs `Social.Comments.SetStatus`; authors give a status only when they write a comment.

## 0.0.1.41 (2026-10-27)

### Added

- Group tree and scope: groups have a parent and a `Code` path (create under a parent, move with `PUT /api/v1/identity/groups/{id}/parent`, delete removes the subtree), members can be added, removed and moved, `IGroupScope` (`Coworkee.Application.Groups`) gives filters the accessible, selected and active groups, `<GroupScopePicker />` lets users narrow lists to some groups, `AddGroupDetailTab` adds tabs to the groups page, group import by parent path. New permissions `Identity.Groups.ManageMembers` (implied by `Manage`) and `Identity.Groups.ScopeAll`; see [identity](../features/identity.md#group-tree-and-scope).
- External databases: `AddCoworkeeExternalDbContext<TContext>()` lets OData sets, row filters, search, export, reports and client entities read entities of a second, plain `DbContext`, and the unit of work saves it; client entities take `ChangedAt` date columns for entities without `IAuditable`. See [persistence](../fundamentals/persistence.md#external-databases).
- Users OData set: `Groups` memberships, filter `Groups/any(m: m/GroupId eq {id})`.
- Groups page: Import (`Groups.Manage`) and Export buttons. The `Groups` set has a computed `ParentPath` ("Customer A/Hamburg"), and the export writes `Name`, `Description` and `ParentPath` in tree order, so the file imports again, also into another tenant.
- Computed OData properties with `Exported = true` are export columns; `ComputedQueryRoot` (moved from Workflows) and `ODataComputedProperty.Inline` help to write subqueries in them.
- Log in as another user: permission `Identity.Users.Impersonate`, *Log in as* on the users list and the user page, *Back to my account* in the user menu. `POST /api/v1/identity/users/{id}/impersonation` issues a one-minute ticket, `/bff/impersonate` and `/bff/stop-impersonation` switch the sign-in through the auth server pages `/Account/Impersonate` and `/Account/StopImpersonation`. Tokens carry `impersonator` and `impersonator_name`, `/bff/user` returns `ImpersonatorId` and `ImpersonatorName`. `[NotWhileImpersonating]` refuses a request while impersonating; see [identity](../features/identity.md#log-in-as-another-user).
- `AuthClientOptions.AllowImpersonation` (`Coworkee:Auth:Clients:<n>:AllowImpersonation`, default off): only such clients serve someone signed in as another user, the others answer `access_denied`. `AddWeb` sets it for the web client; set it for own BFF clients that should support *Log in as*.
- `AuthServerOptions.MaxImpersonationDuration` (`Coworkee:Auth:MaxImpersonationDuration`, 8 hours): an impersonation ends at the next authorization or refresh after it. It also ends when the impersonator signs out everywhere, changes his password, is locked, or the rules no longer hold.
- `AuditQuery.ImpersonatorId` (`GET /api/v1/audit?impersonatorId=...`); `ImpersonatedUser`, `JobEnvelope` and `IntegrationMessage` have an optional `ImpersonatorId`.

### Changed

- Apps need a migration (`dotnet ef migrations add GroupTree`): `cw.Groups` gets `ParentId` and `Code`, the name is unique per parent instead of per tenant, new table `cw.GroupSelections`. The migrations host gives existing groups their codes.
- `SetGroupMembers` needs `Identity.Groups.ManageMembers` instead of `Manage`; roles with `Manage` keep working, it implies the new permission.
- `ICoworkeeApi` has new group members (`GetGroupChildrenAsync`, `SearchGroupTreeAsync`, `MoveGroupAsync`, `AddGroupMembersAsync`, `RemoveGroupMembersAsync`, `MoveGroupMembersAsync`, `GetMyGroupsAsync`, `SelectGroupsAsync`): hand-written implementations and test fakes no longer compile until they add them.
- `PUT /api/v1/identity/groups/{id}` with a `ParentId` other than the current parent answers 400; a group moves with `PUT /api/v1/identity/groups/{id}/parent`. A missing `ParentId` keeps the parent.
- `IUnitOfWork` resolves to a wrapper that saves the external contexts and then the app's context, no longer to the DbContext itself; code that cast it to a DbContext injects the context instead.
- Aspire 13.6.4 (app host SDK, `Aspire.Hosting.*`, `Aspire.Hosting.Testing`), CommunityToolkit Aspire packages 13.6.1, Nextended 10.1.48; `coworkee update` moves the pinned packages.
- Apps need a migration (`dotnet ef migrations add Impersonation`): `cw.AuditEntries` and `cw.OutboxMessages` get the nullable column `ImpersonatorId`.
- `ICoworkeeApi` has the new member `StartImpersonationAsync`, `ICurrentUser` the default member `ImpersonatorId` (null): hand-written implementations of `ICoworkeeApi` and test fakes no longer compile until they add it; own `ICurrentUser` implementations that read claims should return the `impersonator` claim.
- While someone is signed in as another user, the account and consent pages of the auth server answer 403, and changing the own e-mail address, creating API keys, exporting personal data, deleting the own account, choosing the active role and creating or changing webhooks and report subscriptions are refused (`auth.impersonating`).
- `IsLocalPath` of the BFF (`/bff/login`, `/bff/impersonate`, `/bff/stop-impersonation`) refuses return addresses with control characters or backslashes.
- `[Realtime]` still broadcasts to everyone with the view permission; with groups as customers in one tenant, clients of other units see record ids and changed property names. Per-group topics follow.

## 0.0.1.40 (2026-10-25)

### Fixed

- Kibana: the sign-in ended with 500 at `/oauth2/callback` ("id token issued by a different provider"); oauth2-proxy expects the issuer with the trailing slash the auth server puts into `iss`.

## 0.0.1.39 (2026-10-25)

### Added

- Open WebUI: `app.AddOpenWebUI(ollama)` adds a chat over the models of Ollama, signed in at the auth server like the other tools; template option `openWebUI` (`--open-web-ui`, with `--ai-provider ollama`), a card in the Studio and part of the preset *Everything*; deploy tool `openwebui`.

### Changed

- Grafana, WebDataStudio and AspireUI sign in at the app's auth server, with or without Keycloak, like Kibana's proxy: one client `{app}-{tool}` each, administrators of the system organisation only, as admins of the tool. The local administrator login stays. They no longer get clients of the Keycloak realm.
- Auth server: user info endpoint `/connect/userinfo` (sub, email, name, role, tenant). `Coworkee:Auth:DynamicBackchannel` names sign-in and sign-out by the issuer and token, user info and keys by the address asked, so tools in containers reach it over the container network while the browser uses the host address; the app host sets it with the issuer in a local run.
- Grafana 13.2.4 (Nextended 10.1.47); data sources and volumes of 11.5 keep working. **Action:** an app host that pins `Nextended.Aspire.Hosting.AspireUI` (`aspireUI` option) raises the pin to 10.1.47, or restore fails with NU1605.
- Studio: cards of options that do not matter right now stay visible, disabled, with the reason as tooltip; Kibana sits right after Search and is part of the preset *Everything*.

### Fixed

- Auth server: clients that ask for `response_mode=form_post` (ASP.NET Core's OpenID Connect handler, WebDataStudio) got a blank page after sign-in; the content security policy blocked the script that sends the answer on. It allows that one script by its hash now.
- Kibana: the sign-in of a local run sends the browser to the host addresses of the auth server and the proxy; it went to `aspire.dev.internal`, which only containers know. The dashboard no longer links `kibana-server`, which let everyone in without the proxy.
- AspireUI: a local run gives it the volume `{app}-aspireui-data` like a deployment. The shared `aspireui-data-aspireui` let the app started last set the Keycloak sign-in of every other app (404 on `/realms/<other app>/.well-known/openid-configuration`). The old volume stays; delete it with `docker volume rm aspireui-data-aspireui` once nothing uses it.

## 0.0.1.38 (2026-10-25)

### New

- Themes: six new built-in themes, 21 in total, which also differ in where the logo sits: GitHub (dark header with the logo, no logo in the menu), LinkHub (pill buttons through custom CSS, logo in the menu only), Terminal (no logo at all), Slate (dark sidebar with the logo, plain header), Ember and Mint (logo in the header only). Built-in themes can now bring custom CSS, app themes take it from their base. CLI/template `--theme`, Studio cards. See [Built-in themes](../modules/theming.md#built-in-themes).
- Kibana: `options.UseKibana()` puts Kibana next to the Elasticsearch container (template and CLI option `kibana`, Studio card *Kibana*, deploy tool `kibana`). oauth2-proxy in front of it signs in at the app's auth server, with Keycloak when it is on; Kibana's own single sign-on would need a paid Elastic license. New `AuthClientOptions.SystemAdministratorsOnly`: the auth server lets only administrators of the system organisation sign in at such a client. See [Kibana](../hosting/aspire.md#kibana).
- Backups: upload a downloaded zip again (*Upload backup* on `/admin/backups` and in the AirWorks backups app, `POST /api/v1/backups/upload?name=`, `UploadBackupCommand`); it is listed like a backup made here and restored the same way. See [Database backups](../modules/backups.md).

### Fixes

- Reports list: the favourite star and the menu of a report card sit on one line.

## 0.0.1.37 (2026-10-20)

### Breaking changes

- **Migration needed** in apps with `Coworkee.Reporting` (the template has it by default): the new tables `cw.ReportSubscriptions` (report subscriptions) and `cw.OutgoingMailAttachments` (Mailing, files of queued mails). `CoworkeeReportingModule` now depends on `CoworkeeMailingModule`.
- `DateRanges` moved from `Coworkee.Client.Blazor.Reporting.State` to `Coworkee.Contracts.Reporting` (the server uses it too); add the `using` where it is called directly.
- **MudBlazor.Extensions 9.11.6**: apps that pin MudBlazor.Extensions themselves move to 9.11.6.

### New

- Reporting: subscriptions by mail. *Subscribe by mail* on a report mails it daily, weekly or monthly at a time of a time zone, with an own date range and facets if wanted; the mail (`Reports.Subscription`, all languages) in the subscriber's language (else the default language of the organisation) shows KPIs with their threshold colours and the first rows of the other widgets (the latest buckets of a time series), an Excel workbook holds all rows, a link ends the subscription. The job `coworkee-report-subscriptions` runs each one as the subscriber, so their permissions and row filters apply. *Reports › Subscriptions* lists them; the new permission `Reports.Subscriptions` shows those of the organisation. Mailing: attachments (`MailAttachment`, `IMailSender.QueueWithAttachmentsAsync`, `MailEnvelope.Files`) for all four providers; own `IMailTransport`s that ignore `Files` keep working without them. See [Subscriptions by mail](../modules/reporting.md#subscriptions-by-mail).
- Reporting: create and change reports with AI. *Create with AI* on the report list and *Ask AI to change this report* in the designer send a prompt to `POST /api/v1/reports/generate` (`GenerateReportDefinitionQuery`, `Reports.Manage`, needs an AI model): the server builds the instructions from the sources the user may read, checks the answer like a save and asks again with the errors (at most twice); the definition opens unsaved in the designer (`reports/new` for a new one, undo for a change). New assistant/MCP tool `generate_report`; `create_report` and `update_report` are offered to the assistant now (they run after the user confirmed). See [Create a report with AI](../modules/reporting.md#create-a-report-with-ai).
- Reporting: thresholds. `WidgetOptions.Thresholds` (`ThresholdStop`: start value, theme colour `success`/`warning`/`error`/`info` or `#rrggbb`, label) colour KPI values and gauges, pivot cells, the table column `ThresholdColumn` and, with `ColorBarsByThreshold`, bars; every band also shows an icon and its label. The designer has a band editor with *Bands from the target*. See [Thresholds](../modules/reporting.md#thresholds).
- Reporting: switches for viewers. `WidgetOptions.Metrics` (`MetricView`: label, aggregates, number format, extra filter) and `WidgetOptions.Buckets` (`BucketView`: label, group-by field, date bucket, date field) offer alternatives that viewers pick in the widget header of KPIs, charts, pivots and tables; the report is not changed, exports use the choice, and the choice is kept per user in the new user setting `Reports.Views`. Saving checks every alternative. See [Switches for viewers](../modules/reporting.md#switches-for-viewers).
- AirWorks: full-screen app launcher. `Coworkee:AirWorks:Launcher` of the AirWorks host (`menu`, the default, or `overlay`) reaches the desktop as `launcher` of `/bff/user`; the template writes it to the host's `appsettings.json` (option `airworksLauncher`, CLI `--airworks-launcher`, Studio card *AirWorks app launcher*). The launcher lists the administration apps under *Administration* and other apps under their `launcherGroup` (new on `CoworkeeApp`, `entityApp` and `embedApp`, which also takes `shortcut`; `group` stays the section of the administration), with a context menu (*Open*, *Add to desktop*) and a focus trap. Existing AirWorks clients move `@fgilde/airworks` to commit 9657ff3 (AirWorks 0.6.0) in `ClientApp/package.json` to use it. See [App launcher](../web/airworks.md#app-launcher).

## 0.0.1.36 (2026-10-20)

### Breaking changes

- **MudBlazor.Extensions 9.11.5**: apps that pin MudBlazor.Extensions themselves move to 9.11.5.

### Fixes

- Navigation: the filter text typed after the filter chips stays in the filter box when a page is opened; it was emptied while the filter still applied. The fix comes with MudBlazor.Extensions 9.11.5.

## 0.0.1.35 (2026-10-20)

### Breaking changes

- Record attachments: `IRecordAttachments.PutAsync`, `DeleteAsync` and `DeleteAllAsync` no longer save; they join the caller's unit of work (the pipeline saves after a command, other code calls `IUnitOfWork.SaveChangesAsync`). Blobs written for rows that are never committed are deleted at the end of the scope, blobs of removed rows after the commit. App code that called them outside a command saves itself now; reads (`OpenAsync`, `ListAsync`) see saved rows only. The HTTP endpoints save as before. See [Record attachments](../modules/attachments.md#in-app-code).
- **Migration needed** in apps with `Coworkee.Social`: the new table `cw.Reactions` (emoji reactions).
- `Error.Validation(field, message).Message` is the given message instead of *One or more validation errors occurred.*; so is the `title` of the 400 problem details. `code` (`validation`) and `errors` are unchanged; the dictionary overload keeps the generic text.
- **Migration needed** in apps with `Coworkee.Workflows`: the new column `RequestedBy` of `cw.WorkflowApprovals` (who ran the transition into the approval). The escalation of an approval with `NotByRequester()` no longer adds the requester as an approver when they are in the escalation role; approvals started before the migration have no requester stored and are not filtered.

### Fixes

- `coworkee entity add`/`module add`: generated list pages show bool columns as translated *Yes*/*No*, enum columns by their translated label (`EnumLabels`), `DateTimeOffset` columns in local time and `DateOnly` columns as short dates in the user's culture, instead of *True*, member names and `+00:00` offsets. Existing generated pages can take the same `CellTemplate`s.
- Localization: 57 library texts that showed in English in every language now ship in all 22 languages: the setup wizard (labels, steps, hints, messages), the descriptions of the Reports, Chat and report data pages, *No reports yet*, *Chat with {0}*, the permission matrix and resource permissions (*Search permissions*, *User or group*, ...), version history, audit timeline, theme picker, *My settings* and the navigation pins. The literal-key test also checks `l[...]`, both texts of `L[a ? "…" : "…"]` and the texts passed to `PageHeader` and `CoworkeeDataTable`.
- Reporting: report names and descriptions, widget titles and value labels show their translation when the app has a text for them (seeded reports can be localized; the stored text stays the key, `CoworkeeLocalizer.Stored`); a widget added from a preset stores the preset's key instead of the translated title. The donut total and the target line are translated, and category labels of the x axis turn by 45° instead of being cut ("2…", "Back…"). See [Reporting](../modules/reporting.md#seeding-a-report-on-a-source).
- `CoworkeeDataTable`: column headers keep their label on one line, so a wide table scrolls sideways instead of squeezing its last columns ("Kostenstell", "Stat") under the sticky action column.
- Layout: the app title in the app bar stays on one line and ends with an ellipsis on narrow screens instead of wrapping.
- `CoworkeeWorkflow`: the due date of an approval keeps its space after the waiting text ("…listed. Due 25.10.").
- Aspire: `AddMigrations` wires the modules the migrations project references like any service (storage, mail, Redis, search, translation, `app.Modules` entries); files a seed stored went to the local folder of the migrations host before, and opened with 404 in the app. App hosts that wired `CoworkeeModules.Storage` into the migrations by hand can drop that line.
- Dialogs: a side sheet or dialog of the library opened from inside a dialog the app showed with the plain MudBlazor `ShowAsync` (for example the file preview of `RecordAttachment`) stayed invisible; MudBlazor.Extensions applied its options to the outer dialog. The layout now marks dialogs that are already open when a new one is added.
- Reporting: charts over a computed source (`AddReportSource`) with several rows per category and series no longer fail (the in-memory grouping split every row into its own group, the chart then threw on the duplicate keys); chart series add up duplicate categories.
- Record attachments: scopes created with `CreateScope()` (start-up code, the Elsa module, older services) no longer fail to dispose once the attachments module is on (`AttachmentBlobs` is `IDisposable` too).
- Security, social: comments, tags, ratings, reactions and their realtime topics of a type registered with its entity class (`Comments<T>`, ...) now require that the user sees the row through the type's OData set (set permission, organisation, soft delete, `IODataEntityFilter<T>` row filters); before, the permission and an existing id were enough. Tag counts and `GET /api/v1/tags/{entityType}/entities` only count visible rows. Types without an OData set keep the id check; `Authorize(...)` still adds checks on top, and hooks that only repeated the row filter can go. See [Social](../modules/social.md).
- Workflows: for an entity the command added in the same unit of work, `WorkflowEngine.LoadAsync` loads the `Include(...)` references through their foreign key when only the id was set, so `Describe`, guards and actions no longer see `null` there.
- `CoworkeeDataTable`: the sticky action column stays opaque on hovered and striped rows (the translucent row tint is painted over the surface), so long cell text no longer shows through under the buttons, in light and dark mode.
- `coworkee entity add`/`module add`: navigation entries, page titles and permission groups show names of several words as words ("Benefit enrollments", "Time off: Benefit enrollments") instead of the PascalCase plural; dialog titles and permission names already did.
- `coworkee entity add`/`module add`: generated pages pass text keys to `PageHeader` and `CreateText` instead of translated texts, which were translated a second time ("People" → "Personal" → "Persönlich"). Rule: `PageHeader` and `CoworkeeDataTable.CreateText` translate, callers pass keys. Generated pages of existing apps drop the `@L[...]` around these values.
- `coworkee entity add`/`module add`: reference facets get `LabelPath = "it.{Navigation}.{Display}"`, so a navigation named like a Dynamic LINQ keyword (`Parent`, `It`, `Root`, ...) no longer makes the OData set answer 500 when facets are asked for. Hand-written facets on such a navigation add the `it.` themselves. See [OData](../web/odata.md#facets).
- `ResourceCalendar`: a month fits the available width (day columns shrink, narrower resource column) and fades at the right edge when it scrolls; rows grow to a two-line resource cell instead of clipping it; a row with a `UserId` shows the avatar with presence plus `Title` and `Subtitle` instead of the user card without the subtitle.
- Security, workflows: the state, history, comments, transitions, decisions and realtime topic of a workflow now require that the user sees the row through its OData set (set permission, organisation, `IODataEntityFilter<T>` row filters); before, `Workflows.View` and an id were enough. Approvers of the row's open approval still see and decide it. `WorkflowEngine.LoadAsync` takes an optional `WorkflowAccess` (default `See`). See [Workflows](../modules/workflows.md#permissions).
- Template: `App.razor` links the scoped CSS bundle, so `.razor.css` files of the web client apply out of the box (the home page logo uses one). Existing apps whose web client has `.razor.css` files add `<link href="@Assets["MyApp.Web.styles.css"]" rel="stylesheet" />` (their web project name) after the MudBlazor.Extensions stylesheet.
- API tests of an app with `--event-bus rabbitMq` start without a broker: new setting `Coworkee:EventBus:RabbitMq:Enabled` (default `true`); the template fixture sets it to `false`, so events reach the handlers in process. Existing apps add `builder.UseSetting("Coworkee:EventBus:RabbitMq:Enabled", "false");` to their `ApiFactory`. See [Testing](../testing.md).
- `coworkee spec validate` refuses an entity whose plural equals its name (the generated feature namespace hid the entity type and the app did not build).

### New

- Record attachments: realtime topic `attachments:{entityType}:{entityId}` (`AttachmentTopics.Record`) on every added, replaced or deleted attachment, for users who may read a slot of the record; `RecordAttachment` follows it and reloads. Personal data export section `attachments` (the user's uploads, metadata only; erasing keeps the files like the `files` section). See [Record attachments](../modules/attachments.md#realtime-and-personal-data).
- Workflows: `State(...).Approval(ctx => ..., rejected, a => a.ApprovedTo(...))` chooses the approved (and with a second function the rejected) state from the entity when the approval is decided, e.g. a second approval for large amounts behind one submit transition; the possible states are listed with `ApprovedTo`/`RejectedTo` and checked at start-up and at every decision. `ApprovalStep.ApprovedState`/`RejectedState` are `null` for such approvals; `ApprovedTargets`/`RejectedTargets` list the states. See [Workflows](../modules/workflows.md#approvals).
- Blazor: `CoworkeeDetailPanel<TKey>`, the details of a board or list item in a right drawer (full screen on phones) bound to a query parameter (`?item={id}`, `QueryParameter`): links, reload and the back button open and close it; Escape and the close button close it; `Title`, `Actions` and `ChildContent` fragments get the key, `Width`, `KeyChanged`, `Open(key)`. See [Detail panel](../ui/detail-panel.md).
- Record attachments (`Coworkee.Files`, module `CoworkeeAttachmentsModule`, **migration needed** for the table `cw.RecordAttachments`): `services.AddRecordAttachments<LeaveRequest>("LeaveRequests", a => a.Slot("certificate", maxBytes, "application/pdf", "image/*").MultiSlot("documents").WriteRequires(permission))`; endpoints `/api/v1/attachments/{type}/{id}/{slot}` (list, inline or download, upload, delete) whose reads follow the row visibility of the type's OData set, writes also the write permission and `Authorize` checks; `IRecordAttachments` (`PutAsync`/`OpenAsync`/`DeleteAsync`/`DeleteAllAsync`/`ListAsync`) for app code, `IAttachmentScanner` hook (virus scan), retention target `orphaned-attachments`. Client: `IAttachmentsApi` and the `RecordAttachment` component (one slot: upload with `MudExUploadEdit`, view with `MudExFileDisplay`, delete). `ODataEntityAccess.VisibleIdsAsync<T>`: the ids the current user sees. See [Record attachments](../modules/attachments.md).
- Anonymous records: `IAnonymous` (or the base `AnonymousEntity` with random `Guid.NewGuid()` ids) for rows that must not point to their author: no `CreatedBy`/`ModifiedBy`/`DeletedBy`, no audit trail entry, snapshots without `CreatedBy`, outbox/webhook/Elsa trigger messages without user and trace id, `CreatedAt`/`ModifiedAt`/`DeletedAt` cut to the UTC day (`IAnonymous.TimestampPrecision`, `record.Truncate(time)` for own timestamps). See [Auditing](../modules/auditing.md#anonymous-records).
- OData: `services.AddODataChildFilter<TalkItem, Talk>(i => i.Talk)` makes child rows follow the row filters of their parent (visible when `ODataEntityAccess.VisibleAsync<Talk>` contains the parent; subquery on the foreign key), instead of a hand-written filter repeating the parent's. See [OData](../web/odata.md#child-rows-follow-their-parent).
- Blazor: small charts without the Reporting module in `Coworkee.Client.Blazor.Components.Charts`: `CoworkeeBarChart` (horizontal or `Vertical`, values, colours, `ShowPercent`, `Max`), `CoworkeeDonut` (segments, centre label, legend) and `CoworkeeSparkline`; plain HTML/SVG in the theme colours (dark mode), texts readable by screen readers. See [Small charts](../ui/charts.md).
- iCalendar files: `IcsCalendar`/`IcsEvent`/`IcsTime` in `Coworkee.Application.Calendar` (all-day, UTC or `TZID` times, location, URL, status, categories, organizer, attendees; RFC 5545 escaping and UTF-8 safe folding at 75 octets) and `Results.Ics(calendar, fileName?)` in `Coworkee.AspNetCore.Http` (`text/calendar; charset=utf-8`). See [Calendar feeds](../web/calendar-feeds.md) for subscription addresses with a token and caching headers.
- Reporting: own data sources. `services.AddReportSource<HeadcountRow>("HeadcountByMonth", "Headcount by month", permission, async (context, ct) => rows)` registers rows of a plain type (an `IQueryable` of the database or a list, aggregated in memory) that reports list after the entity sets (`ReportSourceDto.Computed`, sum icon in the designer) and aggregate like a set: OData filter parsed against the row type, group by with date buckets, all functions; `ReportSourceContext` carries the request services and the date range of the widget's filter (`DateFrom`/`DateTo`). Tables read their rows from `POST /api/v1/reports/rows` (`IReportsApi.RowsAsync`, new interface member); no data table widget or facets for them, drill-down only when `ReportingOptions.DrillDown` names a page. See [Reporting](../modules/reporting.md#own-data-sources).
- AirWorks (`@coworkee/airworks`): the report viewer and designer handle computed report sources: tables on them read `POST /api/v1/reports/rows`, data tables skip them (the designer offers only entity sets there), the source picker marks them with ∑, the date range of a date-only field filters by plain days. `@coworkee/client` has `ReportSourceDto.computed`, `ReportRowsRequest` and `ReportRowsResult`.
- Social: emoji reactions. `AddCoworkeeSocial(s => s.Reactions<Product>("Products").CommentReactions())`, permissions `Social.Reactions.View`/`.Create`, `GET/POST /api/v1/reactions/{entityType}/{entityId}` (toggle `{ "emoji" }`), realtime topic `reactions:{entityType}:{entityId}`, component `<CoworkeeReactions EntityType="Products" EntityId="id" />` (counts, own ones filled, who reacted on hover, picker of `SocialReactions.Defaults`); with `CommentReactions()` every comment of `CoworkeeComments` gets the same row (`CommentDto.Reactions`, `CommentThreadDto.ShowReactions/CanReact`). Personal data export and erase include reactions. `ISocialApi` has `GetReactionsAsync`/`ToggleReactionAsync`. See [Comments, tags, ratings and reactions](../modules/social.md#reactions).
- Workflows: `WorkflowEngine.LoadAsync` finds an entity the current unit of work added and has not saved yet in the change tracker, so a command adds a record and runs its first transition in one `SaveChanges` (no early save; a refused transition saves nothing). See [Workflows](../modules/workflows.md).
- Background jobs: `TenantJobRunner.ForEachTenantAsync((services, tenantId, ct) => ..., ct)` runs work once per active organisation as its system and in an own scope, logs and returns failed organisations and goes on with the others; `ForTenantsAsync(ids, ...)` for the organisations a query found. `ITenantDirectory.GetActiveTenantIdsAsync`. The `workflow-deadlines` job uses it (an error in one organisation no longer stops the run). See [Background jobs](../modules/background-jobs.md).
- Client: `ClientUser.From(principal)` (`IsAuthenticated`, `UserId`, `TenantId`, `Email`, `DisplayName`) and the scoped `CurrentUserInfo` (`GetAsync()`, `Changed`) registered by `AddCoworkeeClient`; the library components use it instead of parsing `sub`, and the client principal carries the `tenant_id` claim. See [Client](../ui/client.md#current-user).
- Permissions: computed permissions grant a defined permission from data (`services.AddComputedPermission(HrPermissions.LeadsPeople, (c, ct) => db.Employees.AnyAsync(e => e.LeaderUserId == c.UserId, ct))` or `IComputedPermission`), evaluated once per request and user after the role grants; they count for `[RequiresPermission]`, `IPermissionChecker`, OData, MCP and the client's permission list (navigation, `PermissionGate`, policies). `CoworkeeNavItem(..., Visible: sp => ...)` hides an entry from a client-side callback. See [Permissions](../security/permissions.md#computed-permissions).
- `ODataPicker`/`OptionalODataPicker`: `TextProperty="FirstName,LastName"` shows several properties joined with spaces, `$select`s and sorts by them and finds a row when every word of the search appears in one of them; with one property a search of several words now also matches when the words are not adjacent. See [Data tables](../ui/data-table.md#edit-dialogs).
- `ResourceCalendar`: `CalendarResource.WorkDays` overrides the work days for one row (part-time people), and unmatched attributes (`id`, `data-testid`, ...) reach the root element. See [Resource calendar](../ui/resource-calendar.md).
- Workflows: `EntityWorkflow<T>.Include(e => e.Employee)` loads a navigation with the entity, so `Describe`, guards and actions can use it (`Describe(l => $"Vacation of {l.Employee.Name}")`). See [Workflows](../modules/workflows.md#states-and-transitions).
- OData sets: `AddODataEntity<T>(set, permission, o => o.Hide(...).HideUnless(e => e.Salary, permission))` shows a property only to users with the permission; lists, single reads, `$filter`/`$orderby`/`$select`/`$expand`, facets, exports, reports, the global search, the assistant's data tools, the entity history and webhooks respect it. `ODataEntityRegistration.ProtectedProperties`, `ODataEntityAccess.DeniedPropertiesAsync`. See [OData](../web/odata.md#properties-behind-a-permission).
- `ISystemDispatcher.SendAsync(request, permissions, ct)` runs a request as the system in the caller's organisation, with exactly the given permissions and in its own scope (audit: *system*); `CurrentUserScope.BeginSystem(tenantId, permissions)` and `SystemActor` underneath. `AnonymousCurrentUser` and `TestCurrentUser` now pass on the ambient `ClientId`/`ClientPermissions`. See [Permissions](../security/permissions.md#acting-as-the-system).
- `CoworkeeDataTable`: `RowHref` (`Func<T, string?>`) and `OnRowClick` (`EventCallback<T>`) open a row by clicking it (not its buttons or checkbox) or by its open button in the action column (keyboard, real link for `RowHref`). See [Data tables](../ui/data-table.md#row-links).
- `ResourceCalendar` (Blazor): resources against days with bars, half days, lanes, holidays and drag to select days. See [Resource calendar](../ui/resource-calendar.md).
- Workflows: `ApprovalStep.UsersFrom(context => ...)` finds approvers from the entity when the approval starts, e.g. the manager of the requester.

### Fixes

- A stored secret setting that the current Data Protection key ring cannot decrypt (for example after the key ring changed) reads as unset with a warning in the log, instead of failing every read of the settings (`/api/v1/ai/status` answered 500 on every page). Save the secret again. `SettingProtector` takes an `ILogger<SettingProtector>`.

## 0.0.1.34 (2026-10-18)

### Breaking changes

- **Migration needed**: passkeys live in the new table `cw.UserPasskeys` (ASP.NET Core Identity schema version 3: key `CredentialId`, `Data` as JSON, cascade from `cw.Users`). Own `ICoworkeeApi` implementations add `ResetUserPasskeysAsync`.
- **Migration needed** in apps that add the new module `Coworkee.Retention`: the table `cw.RetentionRuns`. Apps without it need nothing.
- `SettingWriter` and `SettingProtector` of `Coworkee.Settings` are public now (for modules that write their own settings).

### New

- Passkeys (WebAuthn) on the auth server: *Sign in with a passkey* and the browser's autofill on the sign-in page, the account page *Passkeys* to add (at most 20), rename and remove them, *Passkeys (n)* in the security tab of the profile, *Passkeys: n* with *Remove all* in the admin sign-in panel (`POST /api/v1/identity/users/{id}/passkeys/reset`), `PasskeyCount` in `ProfileDto` and `UserDetailDto`, passkeys in the personal data export. A passkey sign-in skips two-step verification. The setting `Account.Passkeys.Enabled` (default on) and the login mode *External* turn them off; `Coworkee:Auth:Passkeys` binds `IdentityPasskeyOptions` (`ServerDomain` behind a proxy without forwarded headers). See [Passkeys](../security/passkeys.md).
- The audit trail writes binary keys as base64url in `EntityId` instead of `System.Byte[]`.
- `Coworkee.Retention`: days to keep per kind of data (audit trail, snapshots, processed outbox and inbox, soft deleted records, AI tool calls and conversations, webhook deliveries, mail log, notifications, import and export jobs, deleted files with their blobs, backups, OpenIddict tokens), the nightly job `coworkee-retention` (`Coworkee:Retention:Cron`, default `41 2 * * *`), the admin page *Retention* (`/admin/retention`, permission `Retention.Manage`, system organisation only) with live counts and *Delete now*, endpoints `/api/v1/retention` and a section in the system status. Every kind of data that was kept forever stays so until days are set; only the webhook log keeps its 30 days. Modules and apps add their own with `IRetentionTarget` and `services.AddRetentionTarget<T>()`; `ExecuteDeleteInBatchesAsync` deletes in batches of 1000. Template, CLI and Studio option `retention` (on); new apps keep processed messages and tokens 14 days and import and export jobs 30. See [Retention](../modules/retention.md).
- Webhooks: `WebhookOptions.LogRetentionDays` is the default of `Retention.webhook-deliveries.Days`; with the Retention module the old daily webhook log cleanup does nothing.
- `CoworkeeMigrationsOptions.AfterSeed` runs the app's own data steps after the migration and the seed, before Keycloak is reconciled.

## 0.0.1.33 (2026-10-17)

### Breaking changes

- **Migration needed**: a filtered unique index `IX_Themes_Name` on `cw.Themes` (built-in themes, `TenantId` null) and the column `cw.ChatAttachments.ThumbnailKey`. The index keeps two app instances from seeding the built-in themes twice on SQL Server and SQLite (Postgres already refused it); the seed no longer cleans up such duplicates. If the migration reports a duplicate key, delete the extra built-in row first.
- **History restore checks its preview**: `EntityHistoryPreviewDto.Version` and `EntityHistoryRestoreRequest.Version` are new; `POST /api/v1/history/{set}/{id}/restore` answers `409` when the version is missing or the record changed since the preview. The history dialog, the audit log undo and the AirWorks record window preview again. Own `IEntityHistoryApi` implementations and direct API calls pass the version; regenerate OpenAPI snapshots and SDKs. See [Auditing](../modules/auditing.md#record-history-and-restore).
- **`Coworkee.Chat` depends on `Coworkee.BackgroundJobs`** and brings SkiaSharp (MIT) with its native Linux assets for the thumbnails.

### New

- `coworkee update` inserts single lines itself (rule kind `insertions` in `upgrade-rules.json`: after the first line matching a pattern, with its indentation, skipped where the line is already there; `--dry-run` only lists them). The step of 0.0.1.32 (`builder.ConfigureContainer(new DefaultServiceProviderFactory());` in `src/*.Migrations/Program.cs`) is applied automatically now.
- New apps have an `AGENTS.md` with the rules for coding agents (layout, required patterns, pitfalls, migrations, tests, CLI and MCP) and a `CLAUDE.md` that imports it. Existing apps can copy them from a new app of the same options.
- AirWorks: the theme and mail template editors of the administration are the pages of the web app in a window when the BFF knows the web app (`embedOr`), the TypeScript editors stay the fallback; *Workflow designer* opens Elsa Studio in a browser tab (`Workflows.Elsa.Manage`); the record window has *Open in the web app* (the page of the record's global search hit). New text key `Open in the web app`.
- `Coworkee:Auth:FrameAncestors`: origins that may frame the account pages of the auth server (default none), so an AirWorks window can show the sign-in page of an embedded web page. Same site only; see [AirWorks](../web/airworks.md#pages-of-the-web-app-embedapp).
- `app.UseCoworkeeSecurityHeaders()` (`Coworkee.Bff`) sends a Content-Security-Policy for the Blazor WebAssembly client (frame-ancestors from `Coworkee:Bff:FrameAncestors`, MudEx file previews from blob and data URLs, hot reload in Development), `nosniff` and a Referrer-Policy; extra sources per directive or an own policy through `Coworkee:Bff:SecurityHeaders`. The template's web host uses it; existing apps add it before `UseStaticFiles` and drop a hand-written policy. Apps with the cloud pickers of the Files module add their hosts. See [Security headers of the web app](../security/authentication.md#security-headers-of-the-web-app).
- `Coworkee:Bff:MaxRequestBodySize` caps what the web host forwards to the API; by default its forwarded routes have no limit and the API checks its own. A hand-written `ConfigureKestrel(k => k.Limits.MaxRequestBodySize = ...)` in the web or AirWorks host is not needed and can go.
- `builder.AddCoworkeeMigrations<TDbContext>(...)` and `host.RunCoworkeeMigrationsAsync()` (`Coworkee.Identity.Setup`) are the migrations host: migrate with retries (`Migrations:Attempts`), seed the development users or the first administrator of a deployment, keep the development users in step, reconcile Keycloak, and the container fix of 0.0.1.32. The template uses it; existing apps replace their hand-written `src/*.Migrations/Program.cs` with it, so later fixes come with the package (`coworkee update` points at it). See [Migrations and seeding](../fundamentals/persistence.md#migrations-and-seeding).
- Chat pictures get a small WebP the server makes (at most 480 px, turned upright, `GET /api/v1/chat/attachments/{id}/thumbnail`, `ChatAttachmentDto.HasThumbnail`); the message list shows it, a click opens the full file.
- Every chat file opens in the MudEx file viewer (`MudExFileDisplay`), also an upload before it is sent. The rule for own pages is on [Files](../modules/files.md#showing-files).
- The policy of `UseCoworkeeSecurityHeaders` allows the CDNs the file viewer loads its viewers from (`Coworkee:Bff:SecurityHeaders:FileViewerCdns`: jsDelivr, esm.sh, unpkg) in `script-src`, `style-src` and `connect-src`, so Word, Excel, maps and the other viewers work out of the box; clear the list for a policy without third-party script. See [Security headers of the web app](../security/authentication.md#security-headers-of-the-web-app).
- Files uploaded to a chat but never sent are deleted after `Coworkee:Chat:UnsentAttachmentLifetime` (default 24 hours, an hourly job).

### Fixes

- The SDKs page described an `sdk/openapi.json` with a snapshot test and an `sdk/typescript` client that the template never had; it now shows them as a recipe for apps that need them.
- `npm run build` of `@coworkee/client` and `@coworkee/airworks` empties `dist` first, so packs no longer carry files of removed sources.
- The built-in themes Coworkee, Forest and Sunset meet WCAG AA on button text in light mode (slightly darker primary or secondary color); the contrast test covers every built-in theme.
- The file viewer never hands a private file to the online Office viewer of Microsoft.
- Files forwarded by the web host keep their own sandbox policy: `UseCoworkeeSecurityHeaders` adds the app policy only to answers without one (before, forwarded files carried both).
- The content endpoints of files, public files, chat files and thumbnails answer `HEAD`, which the image viewer sends first (it got 405).


## 0.0.1.32 (2026-10-17)

### Fixes

- The migrations of a local run crashed at start since 0.0.1.31: they run as Development, whose service validation fails on the MVC services of OData in a host without a web environment. The template builds the container of the migrations with the production defaults; existing apps add `builder.ConfigureContainer(new DefaultServiceProviderFactory());` after `Host.CreateApplicationBuilder(args)` in `src/*.Migrations/Program.cs` (`coworkee update` points at it).

## 0.0.1.31 (2026-10-17)

### Breaking changes

- **Migration needed** (apps with Chat): direct and group chats bring the tables `cw.ChatConversations`, `cw.ChatMembers`, `cw.ChatReactions`, `cw.ChatAttachments` and new columns of `cw.ChatMessages`. Earlier direct messages move into conversations at the next start; what was read stays read.
- **`Coworkee.Chat` depends on `Coworkee.Storage`** (attachments) and references `Coworkee.Notifications` (mention notifications when that module is there).
- **Chat contracts**: `ChatMessageDto.ToUserId` is nullable (empty in groups) and the DTO has new properties (`ConversationId`, `Html`, `Source`, `EditedAt`, `Deleted`, `ReplyTo`, `Reactions`, `Attachments`, `HasEntityMentions`); `ChatReadDto` has `ConversationId`. `IChatApi` gained the conversation members; own implementations add them. New endpoints under `/api/v1/chat/chats`, `/messages` and `/attachments`: regenerate OpenAPI snapshots and SDKs. The plain text endpoints by person stay.
- **`UserAvatar` opens a menu with *Chat* and *Profile*** for other people when the user may chat (`OfferChat`, default `true`); set `OfferChat="false"` where the avatar sits in something that reacts to clicks. New library page `person` at `/people/{id}`.

### New

- Eight more built-in themes, each with light and dark palettes, own fonts, radius and options: Octo, Code, Arctic, Vampire, Solar, Atom, Paper and Aubergine (CLI/spec `theme`, template, Studio gallery). Their text meets WCAG AA contrast. See [Theming](../modules/theming.md#built-in-themes).
- The setup token is easy to find in a local run: the app host gives every service one token and, while the system is not set up, prints it on its console (`coworkee run`), in the api's log and in the Aspire dashboard (*Setup* link with the token on the web resource, *Setup token* property on the api). Coworkee Studio shows it with a copy button on the run card and under *My apps*; `coworkee deploy` names `<APP>_SETUP_TOKEN` when no administrator is set. `/setup?token=...` fills the token in. See [Setup wizard or seed](../security/authentication.md#setup-wizard-or-seed).
- `@coworkee/airworks`: *Inbox* (`coworkee.inbox`, approvals and notifications) replaces `notificationsApp`; *Records* with a record window (`coworkee.record`: fields, history with undo and restore, workflow); the profile is the first tab of *Settings* (`profileApp` is gone, `renderProfile` stays); the API documentation moved into the administration (*Monitoring*). Apps that registered `notificationsApp`, `profileApp` or `apiDocsApp` by name take `defaultApps` or the new names.
- Pages of the web app in AirWorks windows: `embedApp`, `openWebPage`; `CoworkeeLayout` shows only the page with `?embed=1`. `BffOptions.WebAppUrl` (reported as `appUrl` of `/bff/user`) and `BffOptions.FrameAncestors` (lets those origins frame the pages; antiforgery's `X-Frame-Options` is suppressed then); `AddAirWorks` sets both under Aspire.
- Badges on the inbox and chat tiles with AirWorks' `setBadge` (AirWorks 0.5; older versions just show none).
- Chat: group chats (name, add and remove people, leave), formatted messages with the rich text editor (Enter sends), `@` mentions of people (link to the person, notification for members of the chat) and `#` mentions of records from the global search (shown only to readers whose search finds them, otherwise *restricted*), replies, emoji reactions, editing and deleting own messages, files and pictures by button, drag and drop or paste with upload progress (members only, setting `Chat.MaxAttachmentMb`, hook `IChatAttachmentScanner`), read marks per reader, typing indicator and search in a chat. The server sanitizes every message. See [Chat](../modules/chat.md).
- Mini chats: a dock at the bottom right with up to three collapsible chat windows (unread count when minimized, *Open in the chat*, kept per user across reloads, full screen on phones). They open from the chat page, the command palette (**Chat with…**, `@name`), any avatar, the person page, the snackbar of a new message and mention notifications. `ChatDock` opens them from own components.
- `RichTextEditor` takes `Tools` (toolbar) and `Placeholder`.

### Fixes

- A new app run locally showed its setup page instead of signing in with the development users: the migrations project has no launch profile and ran as Production, so `DemoSeed` was skipped. `AddMigrations` passes the app host's environment (`DOTNET_ENVIRONMENT`) to the migrations of a local run.
- The setup page of a system that is already set up goes to the app; a setup tab left open leaves for the app when it gets the focus again or another tab finishes the setup.
- Built-in themes were listed twice when requests seeded them at the same moment (SQL Server and SQLite let the unique index repeat null tenants). Seeding runs once at a time per process and removes such copies (the one an organisation uses stays).

## 0.0.1.30 (2026-10-16)

### New

- The audit log undoes changes of entity sets registered with `AddEntityHistory`: per entry *Undo this change*, *Restore the state before this change*, *Restore record* (soft delete) and *Open record history*, through the history preview and restore. `EntityHistoryRestoreRequest` has a `Mode` (`After`, `Before`, `Undo`), the preview marks fields a later entry changed (`ChangedLater`), and `GET /api/v1/history` lists the readable sets. `IEntityHistoryApi` has `GetSetsAsync` (own implementations add it).

## 0.0.1.29 (2026-10-16)

### New

- Workflow state as a server-side filter: every OData set whose entity has a workflow gets the virtual properties `WorkflowState` and `WorkflowAwaitsMe` for `$filter` and `$orderby` (a tenant-filtered subquery, or the `MirrorTo` column) and a facet `WorkflowState` with the states of the definition and *Awaiting my approval*; both need `Workflows.View`. `CoworkeeWorkflowColumn` for `CoworkeeDataTable` filters, sorts and groups by it; `WorkflowState<T>.WithColor(...)` colors a state. Replace `TemplateColumn` + `CoworkeeWorkflowActions` with `<CoworkeeWorkflowColumn T="..." EntityType="..." />`.
- OData sets can carry computed properties of other modules (`IODataComputedPropertySource`, `ODataComputedProperty`): bound into `$filter`/`$orderby`, never serialized, optional facet. The library configures the OData route itself now instead of Nextended's `AddODataAuto`.

### Fixes

- `SyncDevelopmentUsersAsync()` runs for the migrations of a local Aspire run: the migrations project has no launch profile and ran as Production, so the sync never happened there. `AddMigrations` sets `Coworkee:Identity:SyncDevelopmentUsers` in local development runs; the sync runs in Development or with that setting.
- `SyncDevelopmentUsersAsync()` also gives seed users the roles and seeded roles the permissions added to the seed since (Development only, nothing is removed), so a database set up earlier picks up e.g. a new role of a demo user and new workflow permissions of a demo role.

## 0.0.1.28 (2026-10-16)

### Breaking changes

- **Migration needed** (apps with Reporting): new table `cw.DashboardReports`.
- **`IReportsApi`** gained `GetDashboardAsync` and `SetDashboardAsync`; own implementations add them. New endpoints under `/api/v1/reports/dashboard`: regenerate OpenAPI snapshots and SDKs.
- **`AddCommandPaletteEntitySet` and `CommandPaletteEntitySet<T>` are removed**: the command palette finds records through the new global search of the server. Register the set next to its OData set, e.g. `services.AddGlobalSearch<Product>(s => s.Fields(p => p.Name, p => p.Barcode).Page("/catalog/products"))` (`coworkee update` points at the old calls).
- New endpoints `/api/v1/search`, `/api/v1/search/stream` and `/api/v1/search/providers`: regenerate OpenAPI snapshots and SDKs.
- **`ICoworkeeApi`** gained `GetMailTemplateHistoryAsync`; own implementations add it.
- **MudBlazor.Extensions 9.11.4 and MudExRichTextEditor 9.5.5**: apps that pin MudBlazor.Extensions themselves move to 9.11.4.

### New

- A report as dashboard: every user can make a report their dashboard, holders of the new permission `Reports.Dashboard` the organisation's default; the user's choice wins. `ReportDashboard` shows it live and falls back to the app's own dashboard. The template's dashboard page uses it, lists *Dashboard* before *Reports* and the sample setup makes *Catalog overview* the organisation's dashboard. See [A report as dashboard](../modules/reporting.md#a-report-as-dashboard).
- **Global search**: one search over users, roles, groups, files, reports, chats and the app's own sets (`AddGlobalSearch<T>` for OData sets, `IGlobalSearchProvider` for anything else, `AddGlobalSearchIndex` for an Elasticsearch alias). Prefix before part, `"exact"`, `users:max` type filters and ids; scores comparable across providers, a timeout per provider, the same permissions, organisation and row rules as the OData lists. The command palette shows the hits group by group as they arrive, `/search?q=` lists all with a filter per type, `GET /api/v1/search/stream` streams JSON lines through the BFF, and the MCP tool `global_search` hands them to AI clients. See [Global search](../web/global-search.md).
- The files page opens the preview of `?file={id}`.
- Template samples: brands, products and documents are in the global search.
- `coworkee module add` and `coworkee entity add` register generated entities with text properties in the global search (`AddGlobalSearch<T>` with those fields and the list page).
- **Rich text editor setting**: `Editors.RichTextEditor` (TipTap, the default, or Quill) picks the engine of every rich text editor of the library; `RichTextEditor` brings the same choice to own pages. Mail placeholders work as mentions in both. See [Mailing](../modules/mailing.md#editing-a-template).
- **Mail template editor**: the editor fills the window, a preview sheet follows every change (desktop or phone width, light or dark mail client, inbox line), version history with a line diff and restore (`GET /api/v1/mail/templates/{name}/{culture}/history`), a guard against leaving unsaved changes, language chips next to the title and a searchable placeholder list.
- **Workflow designer**: `Coworkee.Workflows.Elsa.Studio` hosts Elsa Studio as an app of its own that signs in at the auth server (permission `Workflows.Elsa.Manage`) and works with the Elsa API of the api; `app.AddElsaStudio<T>()` (or `AddProjects()` for `*_ElsaStudio`) wires the client, the API address and the designer link, `coworkee deploy --without elsastudio` leaves it out. Template option `elsaStudio` (`--elsa-studio`). See [Elsa Studio](../modules/workflows-elsa.md#elsa-studio).

### Fixes

- Dialogs and side sheets of the library (import wizard, campaign wizard, theme, logo, code editor, API key, workflow comment) carry the initial class of MudBlazor.Extensions, so they show up only at their final position instead of flashing in the middle first. Own dialogs opened with `ShowExAsync` or `ShowSideSheetAsync` set `Class="@MudExCss.Classes.Dialog.Initial"` on their `MudDialog`.
- The approvals inbox shows title, details and deadline of an approval on lines of their own.
- Apps with `Coworkee.Workflows.Elsa` keep the property names of their validation errors (`Brand.Name`); the Elsa API (FastEndpoints) had switched FluentValidation to camelCase for every validator (`brand.Name`).
- Elsa's endpoints (`/api/elsa/...`) no longer land in the app's OpenAPI document: regenerate OpenAPI snapshots and SDKs of apps with `Coworkee.Workflows.Elsa`.


## 0.0.1.27 (2026-10-16)

### Fixes

- Links to addresses of the server (e.g. *Sign in* at the end of the setup, `/bff/login`) load from the server again; since the library pages have a catch-all route, the app showed *Not found.* for them. Nothing to change in apps.

## 0.0.1.26 (2026-10-16)

### Breaking changes

- **`ICoworkeeApi`** gained `GetSystemStatusAsync`; own implementations add it.
- **Library pages under their own prefix**: admin pages of the library moved from `/admin/...` to `/cw/admin/...` (`CoworkeeClientOptions.PagePrefix`, `""` keeps the old addresses); old addresses go on to the new ones. The library pages have no `@page` routes any more, one catch-all page (`/{*Path}`) renders them: an app with its own catch-all page gets an ambiguous route. Apps can replace or turn off pages by key (`options.Pages`), see [Replacing components](../ui/customizing.md#pages-of-the-library).
- **Deployments take the app host's tools along**: a published app host now deploys Keycloak, Grafana, WebDataStudio, LibreTranslate and AspireUI too. Apps from an older template copy three things from a new one: in `Migrations/Program.cs` the seed of `InitialAdmin.From(configuration)` (with `DemoSeed.ConfigureDeployment`) and `ReconcileKeycloakRealmAsync()`, in `ServiceDefaults/Extensions.cs` the Grafana exporter per signal with `GRAFANA_OTLP_PROTOCOL`, and in `AppHost.cs` `app.Deploys(...)` for Ollama and `aspireUi.WithCoworkee(app)` for AspireUI. `KeycloakRealm` moved to `Coworkee.Contracts.Identity`.
- **Migration needed**: new tables `cw.PushSubscriptions` (Notifications), `cw.DataTransferJobs` and `cw.ImportProfiles` (OData, so every app). Workflows and Webhooks bring their own tables when an app adds them.
- **`ICoworkeeApi`** gained `AddMailTemplateLanguageAsync`, `GetPushSettingsAsync`, `SavePushSubscriptionAsync` and `DeletePushSubscriptionAsync`; own implementations add them.
- **`Notification`** derives from `AggregateRoot` now (it raises `NotificationCreated` while push is configured). No schema change from that.
- **`IODataClient.ExportAsync` and `ImportAsync` removed**: use `IDataTransferApi` (export of the current view in three formats, the import wizard). Own `IODataClient` fakes drop the two methods.
- **`ODataImportRegistration`** has `Create`, `Update` and `Delete` instead of `Send`; `AddODataImport` takes `(Guid? id, row) => command` and an optional delete command for updates and undo.
- **`CoworkeeODataModule` depends on `CoworkeeBackgroundJobsModule`**: imports and large exports run as jobs. Apps from the template have it already (mailing); test hosts set `Coworkee:Jobs:ConnectionStringName` and create their tables before `UseCoworkee()`.
- The *Import* button of `CoworkeeDataTable` opens the import wizard instead of uploading an Excel file at once; Excel, CSV and JSON exports of OData sets come from the server with the visible columns.
- Phone layout: apps add `viewport-fit=cover` to the viewport tag of `App.razor` (`coworkee update` does it).
- New endpoints (push, mail template languages, import and export, record history, workflows, webhooks): regenerate OpenAPI snapshots and SDKs.

`coworkee update` knows all of these (upgrade rules of 0.0.1.26).

### New

- Template samples: the command palette offers *New product* (opens the dialog through `/catalog/products?new=true`) and finds products by name or barcode.
- **Webhooks: rotate secret**: a new secret shown once, the old one signs next to it for `Coworkee:Webhooks:SecretGracePeriod` (24 h), see [Webhooks](../modules/webhooks.md). Two new columns on `cw.WebhookSubscriptions` (apps with webhooks need a migration).
- **Workflow chips in tables** load the states of a page in one request (`POST /api/v1/workflows/{entityType}/states`, `IWorkflowApi.GetStatesAsync`) instead of one per row; own `IWorkflowApi` implementations add the method.
- **Range sliders in column filters**: *between* on number and date columns slides between the smallest and largest value the server reports; a column with a date or number range facet (e.g. the demo's order date) shows that facet's histogram and slider instead of a check box per bucket.
- **System status** page for the system organisation (`/cw/admin/system-status`): versions, database and pending migrations, outbox, background jobs with the last failures, mail, realtime, Redis, files, translation and AI; modules and apps add cards with `ISystemStatusContributor`, see [Admin UI](../features/blazor-ui.md#system-status).
- **Workflows and approvals** (`Coworkee.Workflows`, optional): states and guarded transitions per entity type, approvals by roles or users with deadline, reminder and escalation, a history, the components `CoworkeeWorkflow`, `CoworkeeWorkflowActions` and `CoworkeeWorkflowTimeline` and the page *Approvals*, see [Workflows and approvals](../modules/workflows.md).
- **Elsa workflows** (`Coworkee.Workflows.Elsa`, optional): Elsa Workflows 3 in the API with Coworkee activities and triggers, see [Elsa workflows](../modules/workflows-elsa.md).
- **Webhooks** (`Coworkee.Webhooks`, optional): endpoints per organisation get signed events (Standard Webhooks style) about changes of opted-in entity sets, workflow transitions and the app's own events, shaped by what the endpoint's owner may read; retries with backoff, switch-off after repeated failures, delivery log, test events and redelivery on `/admin/webhooks`, SSRF protection. See [Webhooks](../modules/webhooks.md).
- **Import wizard** per entity set: CSV, Excel and JSON, detected encoding, separator and header, column mapping with suggestions and saved mappings, conversion with the file's language, preview with the validation of the commands, add, update by key or both, dry run, background job with live progress, file of the failed rows and undo of the last import, see [Import and export](../web/import-export.md). **Admin page** *Import and export* (`/admin/data-transfer`, permission `DataTransfer.Use`; grant it to the roles that should see it). **Exports** of the current view as Excel, CSV or JSON (`format`, `columns`); more than `Coworkee:OData:BackgroundExportRows` rows (10 000) run as a job with a download notice.
- **Record history and restore** (`AddEntityHistory`, `History="true"` on `CoworkeeDataTable`, `CoworkeeEntityHistory`): timeline with word diff, restore of a state or a single field with preview, deleted records of `ISoftDelete` sets come back, see [Auditing](../modules/auditing.md#record-history-and-restore).
- **Command palette** (Ctrl+K): pages, records, actions and the assistant as fallback, extensible with `AddCommandPaletteProvider`, see [Command palette](../ui/command-palette.md).
- **Phone layout** of the Blazor shell and the sign-in pages: overflow menu in the app bar, navigation as overlay, full-screen edit sheets, facet menus as bottom sheets, sticky row actions, 44 px touch targets and safe areas, see [Phones, installable app and push](../ui/phones-and-pwa.md).
- **Installable app**: `<Coworkee.Bff.Pwa.CoworkeePwa />` in the head of `App.razor` links the web manifest in the theme's colours; `Coworkee.Bff` serves manifest, service worker (it keeps only the offline page), icon and offline page; update and install prompts in the app.
- **Web Push** for the notifications: `options.UsePush()` in Aspire, or `Coworkee:Notifications:Push:PublicKey`/`PrivateKey`; users switch it on per device on the notifications page.
- **Languages of mail templates**: *Add language* on Administration > Mail templates adds a variant for a language switched on later, copied or machine translated (texts only, tags and placeholders kept), and removes it again, see [Mailing](../modules/mailing.md#languages-of-a-template). A mail in a language without a variant uses the default language (organisation, system, Languages page) before English (`IDefaultLanguage`); machine translation is available to other modules as `IMachineTranslator`.
- **Deployment**: the app host publishes for Docker Compose, Azure Container Apps or Kubernetes (public addresses, token certificates, setup token, the first administrator and the mail server as parameters, stable volumes, fixed ports); the tools of the app host (Keycloak with its realm, Grafana with the app's dashboards, WebDataStudio, LibreTranslate, Ollama, AspireUI) go along with volumes and public addresses, each one can be left out (`DeployTool`, `coworkee deploy --without`), Mailpit only with `--with mailpit`; `coworkee deploy compose|azure|k8s` asks for the administrator and the mail server and lists every address and the sign-in at the end; Coworkee Studio offers *Deploy* after creating an app; token certificates can come as Base64 from a secret. Only services that process jobs create the Hangfire schema. See [Deployment](../hosting/deployment.md).
- **CLI**: `coworkee add`/`remove` turn template options on or off in an existing app (merged into your files, conflicts as `.rej`, options kept in `.coworkee/app.json`), `coworkee update` applies the upgrade rules of the versions in between, `coworkee db reset|seed|backup|restore`, `coworkee entity import --from db|openapi|csv`, six starter specs (`coworkee specs list|show`, `coworkee new --template-spec`), options `--workflows`, `--elsa`, `--webhooks` and `--pwa` (on by default). MCP tools `add_option` and `import_entities`. See [CLI](../getting-started/cli.md) and [Starter specs](../getting-started/gallery.md).
- **Studio**: open an existing app and change it, *My apps* (start, stop, links), *Start from a template*, entity diagram (drag, draw references, auto layout, PNG export) and entity import with a preview.
- **Template**: sample products are soft deleted and, with auditing, show their history and come back; the dashboard works on SQLite (it returned 500 there); demo users are seeded only in Development, migrations wait for the database.
- **`coworkee doctor`** compares versions inside an app: dotnet-ef with EF Core, Aspire CLI with the app host's Aspire SDK, coworkee with `CoworkeeVersion`, and finds ports of the launch profiles in use, each with a fix.
- **Development users stay in step**: `SyncDevelopmentUsersAsync()` (Development only) adds seed users and roles a database set up earlier lacks and gives seed users their seeded password again; the template's migrations host calls it after `SeedCoworkeeIdentityAsync()`, so changes to `DemoUsers.cs` reach an existing database.

## 0.0.1.25 (2026-10-15)

Fixes only: the loading screen script is its own file, so a strict CSP runs it, and app pages are sent with `Cache-Control: no-cache, no-store`. Nothing to change in apps.

## 0.0.1.24 (2026-10-15)

### Breaking changes

- **`ClientEntityIndicator` removed**: every data table of a mirrored set shows the `ClientEntitySwitch` in its tool bar (added by `AddCoworkeeClientEntities`). Delete the `<ClientEntityIndicator>` lines and an own *Compare* button from your pages.
- **`ODataPicker.Margin`** (and `OptionalODataPicker.Margin`) is `Margin?` now: unset, the picker follows the density of the theme. Code that reads the value as `Margin` adds `?? Margin.Dense`.
- **`CliOption`** (Coworkee.Cli.Spec) gained the constructor parameter `DependsOn`, `OptionDependency` gained `Or`: source compatible, binary incompatible for tools compiled against an older Coworkee.Cli.Spec.
- **`LoadingScreenDto`** gained the positional parameter `SignedIn`; regenerate OpenAPI snapshots and SDKs.
- **Enums in every `MudExObjectEdit`** render with the `EnumEditor` (localized labels from `EnumLabels`, `[Flags]` as multi select of the single-bit members) and nullable enums with the clearable `NullableEnumEditor`. A meta's `RenderWith` still wins.
- **`--sign-in-media` with the layout `Card`** is ignored (not passed, not copied) unless `--loading-screen SignInMedia` shows it behind the loading screen.

### New

- **Loading screen** in the theme's colours with the real download progress, a friendly error state when the start fails, and the user's colour mode, theme and language, see [Theming](../modules/theming.md#loading-screen). **Every app adds one line** as the first line of `<body>` in `MyApp.Web/App.razor`: `<Coworkee.Bff.CoworkeeLoadingScreen Logo="coworkee-icon.svg" />`. Its background (`Animated`, `SignInMedia`, `Plain`) is set on the *Sign-in page* card, with `options.SignIn.LoadingScreen` in Aspire or `--loading-screen` in the CLI. The page is now sent with `Cache-Control: no-cache, no-store`; the logo comes from `/bff/loading-screen/logo`, the script from `/bff/loading-screen/script.js` (no inline script, so apps with a strict Content-Security-Policy keep working).
- **Public files**: managers make a file public (`PUT /api/v1/files/{id}/public`, file manager bar, preview); anyone loads it from `GET /api/v1/files/public/{id}/{name}`, e.g. as sign-in or loading screen media, picked with *Choose from files*. **The model gained `StoredFile.IsPublic`: apps with the Files module add a migration.**
- **Page mode of data tables**: `CoworkeeDataTable.PageMode` (`Pager`, `EndlessScroll`) and the setting `DataTable.DefaultPageMode` (global and per user), see [Data table](../ui/data-table.md).
- **Client entity switch** in the tool bar of mirrored tables: search in the browser or on the server, sync state as a ring, *Compare and choose the faster* picks the faster side; the choice is kept per user.
- `ImageDataUrlEdit` uploads with `MudExUploadEdit`; enums get the `EnumEditor` everywhere (see Breaking changes).
- The texts of the MudBlazor.Extensions components the library shows (upload field, style and cron editors, file manager, file preview) ship in all languages; their stored machine translations are replaced at the next start.
- CLI and Studio: options that need another one (`--airworks-packages` without `--airworks`, `--ai-provider`, `--ai-model` and `--ai-endpoint` without `--ai`, `--backup` off Postgres, `--sign-in-media` with the layout `Card` unless the loading screen shows it, `--sign-in-media-side` without a media) are no longer passed to the template; the CLI warns (`coworkee spec validate` too) and the Studio hides their cards. Sign-in media of a `Card` layout is therefore not copied into the app any more. New `coworkee spec show` and a spec from the Studio that lists every library module with `true`/`false`; `coworkee options` prints `Needs:` and the websites of the products an option brings, the Studio cards show icons and open buttons, see [CLI](../getting-started/cli.md#options).

## 0.0.1.23 and earlier

### Breaking changes

- **`IMailSender`**: the interface now requires the six-parameter `QueueAsync(to, template, model, culture, campaignId, cancellationToken)`. The five-parameter overload is a default method that passes `null` as the campaign. Own implementations (test doubles, wrappers) implement the six-parameter method; callers are unchanged.
- **`IMailTemplateRenderer`**: gained `RenderContentAsync(content, layout, model, cancellationToken)`, which renders with an already resolved layout (the campaign job uses it to resolve template and layout once per run). Own implementations add the method; callers are unchanged.
- **Positional records gained parameters**: `MailTemplateSummaryDto` (`IsCustom`, `Description`), `MailTemplateDto` (`IsCustom`) and `OutgoingMailDto` (`CampaignId`). Code that constructs or deconstructs them positionally, and generated clients, need the new parameters. Regenerate OpenAPI snapshots and SDKs.
- **Removed with the MudEx 9.11.3 adoption**: `ChipListEditor`, `CronEditor`, `CronSchedule` and `SettingsFormRules.WrapInColumns`. Lists of strings render as `MudExTagField<string>` and `[Cron]` renders `MudExCronEdit`; use those directly.

### New

- AirWorks client: chat, reports with their designer, assistant, the administration apps (`adminApps`) and realtime, see [AirWorks client](../web/airworks.md). Generated apps take `@coworkee/client` and `@coworkee/airworks` from the tarballs inside the template; every release also publishes them to npm (`--airworks-packages npm`). `coworkee new --template-source` adds the local package folder to the app's `nuget.config`. Apps made with the first AirWorks version: add `...adminApps` to `ClientApp/src/main.ts` and, for the file preview, the MudEx hosting of the template (`mudex-webcomponents`, `vite.config.ts`, `.dat` content type in `Program.cs`).
- Mail campaigns with recipient presets, saved queries and manual selection, scheduled sending and an unsubscribe confirmation page, see [Mailing](../modules/mailing.md).
- Own mail templates with a visual and a source editor, and a Monaco based `CodeEditor` component.
- Entity model changes for campaigns, recipient lists and own templates: add a migration to your app.
