# Changelog

Changes that need action in apps built on Coworkee. Newest first.

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
