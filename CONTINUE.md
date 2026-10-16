# CONTINUE – handover for the next agent

State of the Coworkee library as of 2026-10-16: everything below is merged on `main` and pushed. Released: **0.0.1.28** on nuget.org and `@coworkee/client` / `@coworkee/airworks` `0.0.1028` on npm (trusted publishing works). 0.0.1.24 = round of 2026-10-15, 0.0.1.26 = round 7, 0.0.1.27 = link fix, 0.0.1.28 = round 8 (dashboard report, global search, editors, Elsa Studio). Demo and Sharemee are on 0.0.1.28 (`master`, pushed).

## Repositories
| Repo | Local path | Remote / branch |
|---|---|---|
| Library (this repo) | `C:\dev\privat\github\coworkee` | fgilde/CoworkeeLib, `main` |
| Demo app | `C:\dev\privat\github\cleanarchitecturebaseblazor` | fgilde/Coworkee, `master` |
| Sharemee (product on the lib) | `C:\dev\privat\github\shareme` | `master` |
| Nextended | `C:\dev\privat\github\Nextended` | `main` (owner releases it; 10.1.44 is current) |
| MudBlazor.Extensions | `C:\dev\privat\github\MudBlazor.Extensions` | owner releases it; never push |

Old worktrees under `C:\dev\privat\github\wt\` are disposable.

## Rules of the owner
- Never add Co-Authored-By / "Generated with" / any attribution to commits or PRs.
- Docs and website never mention "ABP" or "CleanArchitectureBase(Blazor)".
- Repos stay private. Never print seed passwords or keys.
- Code style: CCD, `.razor` + `.razor.cs`, small files, few comments, `// ponytail:` marks deliberate shortcuts. Docs EN + `.de.md` (real umlauts).
- Answer the owner in German, short.

## How to build, test, release
- Build/test: `dotnet build Coworkee.slnx -c Release`, `dotnet test --solution Coworkee.slnx -c Release --no-build` (~1500 tests, Docker needed). Provider runs: `COWORKEE_TEST_DATABASE=SqlServer|Sqlite`.
- Local packs: `pwsh build/pack-local.ps1` → `artifacts/nuget` (0.1.0-dev.*). Demo and Sharemee consume `../coworkee/artifacts/nuget` with `0.1.0-*`; after packing run `dotnet restore --force-evaluate` there.
- CLI template tests with real app builds: `COWORKEE_TEST_FEED=<absolute path>/artifacts/nuget dotnet test tests/Coworkee.Cli.Tests -c Release`.
- **Whenever a module changes the EF model:** run `pwsh templates/Coworkee.Templates/update-migrations.ps1` (template initial migration), and add a migration in demo/Sharemee.
- OpenAPI snapshots: demo `MYAPP_UPDATE_OPENAPI=1`, Sharemee `SHAREME_UPDATE_OPENAPI=1` + `npm run generate` in `sdk/typescript`.
- Docs: `python -m mkdocs build --strict -f website/mkdocs.yml -d <tmp>`; published by CI to https://fgilde.github.io/CoworkeeLib/.
- Release: `gh release create 0.0.1.X --target main` → release workflow packs the slnx and pushes to nuget.org (incl. Coworkee.Cli and Coworkee.Studio tools).
- Changelog convention: new entries go under `## Unreleased` (EN, `website/docs/reference/changelog.md`) and `## Unveröffentlicht` (DE, `changelog.de.md`) at the top; create the heading when it is missing. After a successful release the workflow job `changelog` runs `build/release-changelog.py <version>` (renames both headings to `## <version> (<date>)`, idempotent), commits `docs: changelog for <version>` as github-actions[bot] to main and starts the docs workflow, so the release line of the website (`website/hooks/release.py`, newest `## x (date)` heading) stays right. Never write a version heading by hand before the release. If main is protected, allow github-actions to push or move the heading by hand with the script.

## What exists (short)
Modules (identity, OpenIddict auth server with Card/Split/Slide sign-in, settings, theming (Ocean default), localization + machine translation (LibreTranslate default, MyMemory, DeepL, Azure, Google, AI), realtime incl. presence/active role, chat, files, reporting (GridStack), facets + generic QueryBuilder, client entities, AI (14 providers, admin page `/admin/ai-settings`), MCP server `/mcp`, personal API keys, assistant window), DB providers Postgres/SQL Server/SQLite, optional Redis, Aspire integration (Keycloak with realm reconcile, Grafana, WebDataStudio, LibreTranslate, Ollama), CLI `coworkee` (new/spec/options/module/entity/ui/mcp/self-update), desktop wizard Coworkee Studio (Avalonia, `coworkee-ui`), website with landing page.

## Owner issues 1-3 (fixed, on main)
1. **VS debug of a generated app** ("Unable to launch browser: Could not open wss://…/_framework/debug/ws-proxy"): the web host lacked `app.UseWebAssemblyDebugging()` in Development, so the `inspectUri` of the web launch profile hit a 404. Template, demo (branch `fix/debug-sqlite-aspireui`, demo commit on master) and Sharemee (`8c99fdf`, master) fixed; verified with a generated app: CDP through `/_framework/debug/ws-proxy` works.
2. **WebDataStudio + SQLite `disk I/O error`**: EF Core creates SQLite databases in WAL mode, and WAL's shared-memory index does not work across the Docker Desktop bind mount. With `UseWebDataStudio` the app host now switches/creates the file with `journal_mode=DELETE` before the app starts (EF leaves existing files alone). Verified: generated sqlite app, studio browses `Users`.
3. **AspireUI + Keycloak**: `aspireUi.WithCoworkeeSignOn(app)` (branch `feat/aspireui-signon`, docs in hosting/aspire.md) registers `{app}-{resource}` with `/api/auth/sso/callback`, uses the container authority + `KC_HOSTNAME`, `roles` as groups, `Administrator` as admin group and forces the seeded settings. Works with released Nextended 10.1.44; verified (Keycloak sign-in, admin). The owner's own volume `aspireui-data-aspireui` holds `OidcAuthority={keycloak.bindings.http.url}/realms/gildestore` (a ReferenceExpression passed as string; AspireUI seeds only empty settings) - clear it or use `WithCoworkeeSignOn`. Nextended main has a new `WithSingleSignOn(ReferenceExpression authority, …)` overload (unreleased, not needed by the lib).
   - Later: `options.UseAspireUI()` like Grafana/WebDataStudio (own volume per app).

## Owner round 2026-10-13 (on main)
Spec `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md`, plans `docs/superpowers/plans/2026-10-13-p1..p9-*.md`.
- Settings: no reload loop, mask only names ending in a secret word, `EnabledWhen` conditions, groups with an own `page` (AI, Mail) leave the tabs, `SettingType.Language` select, choices translated, one shared ObjectEdit setup (`SettingsFormRules.FormAttributes`, multi expand), `Lock` (disabled + lock icon) vs new `Protect` (MudEx edit confirmation), import ignores locked/protected/secret values, `StickyMergeTabs` (tab bar + toolbar merge while scrolling).
- Auth: setting `Auth.LoginMode` (only with an external provider; operator escape hatch `Coworkee:Auth:External:ModeOverride`), password rules as Global settings enforced by `SettingsPasswordValidator`, hints on all password fields, setup status carries the rules.
- OAuth apps: `ChipListEditor` (free text + suggestions) for scopes/redirects/resources.
- Mail: `IMailTransport` (SMTP, SendGrid, Mailgun, Mailchimp Transactional) replaces `ISmtpTransport` (breaking), `Mail.UseOwnServer` per organisation, page `/admin/mail-settings` with test mail.
- Services: public `IServiceDirectory` + `IServiceSource`, AspireUI source (`Coworkee:AspireUI:Url/ApiKey`, injected by Aspire for `WithRetrieveApi`), `GET /api/v1/services` returns `{ services, unavailableSources }` (breaking). Nextended 10.1.45.
- Languages: 23 shipped (lib, auth pages, mails, template app), `ShippedLanguagesTests` + literal-key test, `build/localization-check.ps1 -Lang xx`, template copies only the chosen languages, `coworkee texts export`. Default active languages stay en,de. zh-Hant does not fall back to zh.
- Client.Blazor tests: flaky culture/timing causes fixed (`TestCulture`, serial culture tests).
- Demo (master): currency select + Protect, AspireUI in the app host with `WithRetrieveApi`, OpenAPI snapshot + TS SDK regenerated.
- MudEx local branch `feat/coworkee-p4` (not pushed): `MudExCronEdit` + sample `/cron-edit`, `RenderWith` keeps the wrapper, conditional attributes reset. CSS of the cron edit was appended by hand to `mudBlazorExtensions(.min).css` (no sass here) - recompile.

## Owner round 2026-10-14 (on main)
Spec `docs/superpowers/specs/2026-10-14-round2-design.md`.
- CLI/Studio option `aspireUI` (template only, no lib API): inserts the demo's `AddAspireUI()...WithCoworkeeSignOn(app).WithRetrieveApi()` block + package ref.
- Theme `Logo` (was `LogoSvg`, EF rename; SVG or png/jpeg/webp data URL, 512 KB), picked via `ThemeLogoEdit` (MudExUploadEdit dialog), also favicon. Demo migration `ThemeLogo`.
- Keycloak login theme `coworkee-{app}` (KeycloakLoginTheme writer, CSS+JS over keycloak.v2): Aspire bind-mounts `{AppHost}/.coworkee/keycloak-themes`, writes it at start, realm gets `loginTheme`/`displayName(Html)`; API rewrites it after the default theme or sign-in design changes (system organisation only), card on Admin → Themes. Production: shared volume + Keycloak restart.
- OAuth apps: Duplicate action. Mail settings under Communication. Settings tabs flush with the content card, tools right-aligned.
- MudEx: cron branch merged by the owner; new local branch `feat/tagfield-suggestions` (worktree `C:\dev\privat\github\wt\mudex-tags`, not pushed): `MudExTagField` `SearchFunc`, `ValidateChip`, `IsChipClosable`, `CommitOnBlur`, changelog 9.11.3.

## MudEx 9.11.3 adopted (on main)
- `RenderAsChips` renders `MudExTagField<string>` (`CommitOnBlur`, `SearchFunc`, `ValidateChip`, `IsChipClosable`); `ChipListEditor` deleted (breaking for apps that used it directly).
- `[Cron]` renders `MudExCronEdit`; `CronEditor`/`CronSchedule` deleted.
- `SettingsModel` uses `AsDisabledIf`; `SettingsFormRules.WrapInColumns` and the re-wraps after `RenderWith` are gone; `EditDialogExtensions` no longer calls `UpdateAllConditionalSettings()`.

## AirWorks client (phase 1, on main)
Spec `docs/superpowers/specs/2026-10-14-airworks-client-design.md`, plan `docs/superpowers/plans/2026-10-14-airworks-client.md`, report `docs/superpowers/airworks-report.md`.
- Option `airworks` (CLI `--airworks`, Studio card under General with a link to the AirWorks docs, `CliOption.InfoUrl`): project `MyApp.AirWorks` (ASP.NET host with Coworkee.Bff + `ClientApp` built by vite into wwwroot, `-p:SkipClientBuild=true`), `app.AddAirWorks<...>()`, About credit `AboutCredit.AirWorks("0.4.0")`, LOGIN/README/CI (setup-node).
- Sign-in: the host is a second BFF client of the auth server (`{app}-airworks`), so Keycloak works through the auth server exactly like for the Blazor app. `BffOptions.CookieName` (Aspire sets `{app}-airworks`) because localhost cookies ignore ports.
- npm packages in `typescript/` (workspaces; `packages/` is ignored by the NuGet rules of .gitignore): `@coworkee/client` (OpenAPI types from `openapi.json`, `npm run openapi -- <url> [more]`, `npm run generate`), `@coworkee/airworks` (useCoworkee, apps profile/settings/files/file-preview (MudEx web components from jsDelivr)/data/entityApp/notifications/api-docs). `npm test` (node --test), `npm run build`. Not published: `Coworkee.Templates.csproj` runs `typescript/scripts/pack-template.mjs` and ships the tarballs in `ClientApp/vendor` (shortcut). AirWorks itself: `git+https://github.com/fgilde/AirWorks.git#d295c65`; no AirWorks change was needed.
- About (independent of the option): Nextended version via build-time `AssemblyMetadata("PackageVersion:Nextended.Core")`, no URL text in the credit tiles, `AboutCredit.IconBackground`.
- Verified: generated app in Aspire, sign-in with the password and through Keycloak into the AirWorks host, files/OData/settings/notifications/swagger through its BFF (HTTP script, no browser).
- .NET packages 10.0.13 + System.Security.Cryptography.Pkcs/Xml 10.0.13 (advisories of 2026-10-13 broke every build with NU1903); demo and Sharemee bumped too.

## AirWorks client phase 2 (on main)
Report `docs/superpowers/airworks-report-2.md`.
- `@coworkee/airworks`: chat, reports with viewer and full designer (grid, all widget types, data/filter/display, undo/redo, settings, date range), assistant (streamed), `adminApps` (Administration hub + users, groups, roles/grants, applications, scopes, API keys, themes, languages, translations, mail templates/editor, campaigns, recipient lists, mail log, mail/AI settings, backups, services, editions, tenants, audit, AI tool calls), built on `crudApp` + `<dialog>` forms + server-side `lookup` pickers; realtime via `coworkee.realtime`; permissions like the Blazor nav.
- File preview runs in `mudExFrame()` (iframe on `/mudex/coworkee-frame.html` next to the self-hosted MudEx bundle): MudBlazor CSS never reaches the desktop.
- Packages: template default `airworksPackages=bundled` (tarballs in `ClientApp/vendor`, no npm registry needed); `--airworks-packages npm` takes the release version from npm (`typescript/scripts/npm-version.mjs`, `a.b.c.d` -> `a.b.(c*1000+d)`). `release.yml` job `npm` publishes both packages (trusted publishing, `NPM_TOKEN` fallback, never fails the release).
- `coworkee new --template-source` adds the template's folder as `coworkee-local` to the app's `nuget.config`.
- **Owner, once (optional, blocks nothing):** npm org `coworkee`, first publish with secret `NPM_TOKEN` (or by hand), then trusted publisher (GitHub Actions, `fgilde/CoworkeeLib`, `release.yml`) for both packages; steps in the report.
- Browser test (Playwright) of a generated app: scripts in `%TEMP%\opencode\pw` (`apps.mjs`, `designer.mjs`, `http.mjs`), screenshots in `%TEMP%\opencode\airworks-shots`.
- `typescript/scripts/pack-template.mjs` reinstalls the packages when `package-lock.json` is newer than the last install.
- Demo: `src/MyApp.AirWorks` (packages from npm `0.0.1023`, no lock file: npm writes the AirWorks git dependency as `git+ssh`), own apps in `ClientApp/src/apps` (catalog, product window, sales dashboard via OData facets); docs section "Write your own AirWorks app" in `web/airworks*.md` quotes them, keep both in step.

## Mail campaigns, own mail templates and editors (on main)
Spec `docs/superpowers/specs/2026-10-14-mail-campaigns-and-editors-design.md`, plans `docs/superpowers/plans/2026-10-14-{a,b,c}-*.md`, polish report in the owner's `wt/polish/polish-report.md`.
- Own templates (`CustomMailTemplate`, content per culture in `MailTemplateOverride`), `IMailTemplateCatalog`, variables derived from the sample model (`MailTemplateVariables`, skips indexers, lists `campaign.name` also without a campaign), page `/admin/mail/templates`. Body editor `MailBodyEditor`: Visual (MudExRichTextEditor, `{` mentions, stored as plain Scriban) or Source (`CodeEditor`, Monaco, completions after `{{`). The mention list accepts letters, digits, `.` and `_` through `widenMentionChars` in `coworkee.js` (the package only allows letters); no change to MudExRichTextEditor.
- `CodeEditor` (Monaco loaded on demand, falls back to a read-only `pre`), applies Language/ReadOnly changes, one completion provider per language and trigger; `CssRulesEditor`/`CssStylesheet` (rules or raw CSS; the parser knows no escaped braces or `<!--`, see its `shortcut:` comment).
- Campaigns: wizard (template, recipients, send/schedule), recipient presets/saved lists/queries/manual selection, `MailCampaignJob` (claims `Scheduled`, queues one mail per recipient page by page, resolves template and layout once per culture, retries the final status write once, then marks the campaign `Failed`), unsubscribe page, drafts can be reopened, the list stops polling an unclaimed campaign after `ClaimTimeout` (10 min) and shows a warning.
- Limits kept on purpose: a worker shutdown mid-run leaves the campaign in `Sending` (no resume); the opt-out lists are subqueries of every recipient page query (`shortcut:` in `MailRecipientDirectory`); campaign mails need `Coworkee:Notifications:PublicAppUrl`; web and worker must share the Data Protection key ring (unsubscribe tokens); a saved campaign with ticked users keeps them as saved when reopened (the table cannot show them again).
- Demo and Sharemee: migrations, OpenAPI snapshots and TS SDKs are current.
- Browser-verified in the demo (Playwright): own template, `{` mention, Source mode with Monaco + completions, CSS rules/raw editor, campaign wizard sending 3/3 (Mailpit), unsubscribe confirm page + POST through the web app. Two bugs found and fixed there: Monaco defines `window.monaco` asynchronously (`loadMonaco` waits now), and the BFF rejected the unsubscribe form post without `X-CSRF` (`BffOptions.CsrfExemptPaths`, default `/api/v1/mail/unsubscribe`).

## Owner round 2026-10-15 (released in 0.0.1.24)
Spec `docs/superpowers/specs/2026-10-15-loading-tables-demo-design.md`, reports `docs/superpowers/r15-loading-report.md`, `r15-tables-report.md`, `r15-studio-report.md`.
- Loading screen (`Coworkee.Bff.CoworkeeLoadingScreen`, one line in every app's `App.razor`): server-rendered in the theme (user's own theme/mode/language via cookie for signed-in users, default theme for anonymous), real WebAssembly progress, animated / sign-in media / plain background (`SignInDesign.LoadingScreen`, CLI `--loading-screen`), reduced motion, immediate error state on a failed start, logo from `/bff/loading-screen/logo` (cacheable), app page `no-store`; hides when a layout claims it (`[data-coworkee-loading-claim]`) after the theme, otherwise after the first render.
- Public files: `StoredFile.IsPublic` (EF change, migration in apps with the Files module), toggle + badge + copy link in the file manager, anonymous `GET /api/v1/files/public/{id}/{name}`; logo and sign-in media can be picked from the files.
- Data tables: `PageMode` (`FromSettings`/`Pager`/`EndlessScroll`, virtualized), setting `DataTable.DefaultPageMode` (Global + User), grouping falls back to the pager.
- Client entities: `ClientEntitySwitch` (icon + sync ring, browser/server, compare -> toast -> faster mode, per user) replaces `ClientEntityIndicator` (breaking); faster local queries, dropdown facets keep column filter choices, `DateTimeOffset` column filters, synchronous dispose of the IndexedDB storage.
- Edit dialogs keep `RenderWith` editors (e.g. `ODataPicker`) in the grid; enums render through `EnumEditor` with localized labels; product images through `ImageDataUrlEdit` (`MudExUploadEdit`); theme logo dialog takes `UploadRequestDataLoaded`.
- MudEx texts (upload, style editor, cron, ...) ship in all 22 languages; at start-up never-edited stored machine translations of keys that are now shipped are removed.
- CLI/Studio: `CliOption.DependsOn` (e.g. `airworksPackages` only with `airworks`), icons and product links on every card, the final spec lists every library module, `coworkee spec show`.
- AirWorks: own Material glyph icons (`icons` export of `@coworkee/airworks`); the demo has `src/MyApp.AirWorks` with three hand-written apps (catalog, product window, sales overview) and the docs a "Write your own AirWorks app" section.
- Demo: migration `PublicFiles`, loading screen line, client entity switch, OpenAPI/SDK; Sharemee: loading screen line, OpenAPI/SDK (Sharemee does not use the Files module, no migration).
- The demo's AirWorks client uses `@coworkee/client`/`@coworkee/airworks` `0.0.1024`; bump it after each release (`src/MyApp.AirWorks/ClientApp/package.json`).
- Verified in the browser (demo): loading screen with progress, product and order dialogs aligned, Region filter, client switch compare toast, endless scroll, logo upload and pick from files, translated upload texts, AirWorks desktop with the demo apps.

## Round 7 (released in 0.0.1.26)
Branches merged: mail-langs, workflows, mobile-pwa, import-export, history-palette, webhooks, cli-add, deploy, gallery, entity-import. Changelog *Unreleased* is one list (breaking first); `upgrade-rules.json` has them as `0.0.1.26` (the release workflow renames the changelog heading). Template initial migration regenerated with everything in; `defaultModel` excludes workflows and webhooks (pwa/elsa need no change: push table comes with notifications, elsa needs workflows).
- **Demo and Sharemee need**: a migration (`cw.PushSubscriptions`, `cw.DataTransferJobs`, `cw.ImportProfiles`), `viewport-fit=cover` in `App.razor`, OpenAPI snapshots/SDKs.
- Template samples: products are `ISoftDelete` and, with `auditing`, registered with `AddEntityHistory` (`MyAppDatabaseModule`) and `History` on the products table; brand delete counts soft deleted products. Dashboard groups months in memory (SQLite could not translate `DateTimeOffset.Year`, was 500).
- Known: a second `coworkee new --template-source <same .nupkg>` in the same template hive fails (`dotnet new install --force` of the same version leaves "Sequence contains more than one matching element"); a fresh `DOTNET_CLI_HOME` works. `Coworkee.Cli.Spec` now pulls Npgsql/SqlClient/Sqlite (entity import): about 40 of the 113 MB unpacked CLI tool.
- Starter specs do not turn on `workflows`: the spec cannot declare workflows, the module alone would be empty.

- Branch `feat/tooling-smalls`: Studio German says *du* (test `German_texts_say_du`); `SyncDevelopmentUsersAsync()` (Identity, Development only: missing seed users/roles, seeded passwords again) called by the template's migrations host; Sharemee can replace `DemoUserTopUp` with it. `coworkee doctor` compares dotnet-ef/EF Core, Aspire CLI/app host SDK, coworkee/`CoworkeeVersion` and finds busy launch ports.

### Workflows
Docs `modules/workflows.md`, `modules/workflows-elsa.md`.
- `Coworkee.Workflows`: state machine per entity type declared in code (`AddCoworkeeWorkflows(w => w.Entity<T>(...))`), guards (permission, role, condition, comment), actions (`Then`, `OnEnter/OnExit`, `Set`, `NotifyRole`), approvals (roles/users, any/all, four eyes, `DueIn`, `RemindAfter`, `EscalateTo`, `OnTimeout`) via the recurring job `workflow-deadlines`, history table, domain events `WorkflowTransitioned`/`ApprovalStarted`/`ApprovalDecided` (outbox). Tables `cw.Workflow*` (apps need a migration).
- Client: `CoworkeeWorkflow`, `CoworkeeWorkflowActions` (rows batch their states: `WorkflowStates`, `POST /api/v1/workflows/{type}/states`), `CoworkeeWorkflowTimeline`, page `/workflows/approvals`, nav entries Approvals and Workflow designer.
- `Coworkee.Workflows.Elsa`: Elsa 3.9.0 (MIT, net10 builds) in the API host, tables in the app database (Elsa's own migrations), API `/api/elsa` behind a gate for `Workflows.Elsa.Manage` (Elsa's endpoint security disabled), activities SendCommand/RunTransition/WaitForApproval/Notify/QueryOData, triggers WorkflowStateReached/EntityChangedTrigger (`AddCoworkeeElsa(e => e.Watch<T>(...))`). Elsa Studio is not embedded and no host ships yet (see the docs page for why and the plan); `Coworkee:Workflows:Elsa:StudioUrl` feeds the designer link.
- Template/CLI/Studio options `workflows` and `elsa` (off); with samples: product approval (`ProductApproval.cs`, products column) and `ProductApprovedWorkflow` in the API. A generated app with both builds (`COWORKEE_TEST_FEED`); not run in Aspire or a browser yet.
- Not done: personal data export of approver comments. Workflows and Workflows.Elsa tests run in the CI provider matrix (SQL Server, SQLite).

### Import and export
Docs `website/docs/web/import-export*.md`, changelog section *Unreleased*.
- Server in `Coworkee.OData/Transfer` (+ `Import/`): `ImportFileReader`/`CsvFile` (CSV/TSV/XLSX/JSON, encoding/separator/header detection), `ImportField` (writable simple properties of the row type, `XxxId` + navigation `Xxx` of a registered set = lookup by display text), `ImportValueConverter` (culture, ISO dates, enum labels and translations, yes/no words), `ImportColumnMatcher`, `ImportPlanner` (key lookup, updates start from the stored row, FluentValidation of the commands, permission of the command type), `ImportService` (draft job holds the upload), `ImportJob` (one scope per row, never rethrows, undo deletes created ids), `DataExport(s)`/`ExportJob` (xlsx/csv/json, `columns`, job above `BackgroundExportRows`), `ImportProfiles`, `DataTransferJobs`. Entities `DataTransferJob` (IHasRealtimeTopics -> user topic = live progress) and `ImportProfile`, schema `cw`.
- `CoworkeeODataModule` now DependsOn `CoworkeeBackgroundJobsModule`. Test hosts with OData need `Coworkee:Jobs:ConnectionStringName`/`RunServer=false` and EnsureCreated before `UseCoworkee()` (Hangfire's schema made EnsureCreated skip). OData tests run jobs inline (`InlineBackgroundJobs`).
- `AddODataImport(set, (Guid? id, TRow row) => cmd, id => deleteCmd)` enables update/upsert/undo; template sample sets use it. `IODataClient.Export/ImportAsync` removed (breaking), client `IDataTransferApi` + `DataTransferWatcher`, wizard `Components/Data/Import`, page `/admin/data-transfer` (permission `DataTransfer.Use`).
- Limits (`ponytail:`): uploads/results in the job row; one query per key and lookup text; undo only deletes created rows; preview checks the first 200 rows (dry run checks all); no persistent notification (Notifications module is above OData), only the realtime snackbar.

### UI smalls (branch `feat/ui-smalls`)
- Library pages have no `@page` any more (except `/setup`): `Navigation/CoworkeePageHost` (`/{*Path}`) renders them from `CoworkeePages` (keys in `LibraryPages`, reporting registers its own). Admin pages under `CoworkeeClientOptions.PagePrefix` (`/cw` → `/cw/admin/users`, `""` = old), `options.Pages.Replace/Disable`, old addresses redirect; nav, palette, notification links and in-lib links go through `CoworkeePages.Href`. Unknown addresses: Router `NotFoundPage` if set, else the host's "Not found." (the Router's `NotFound` fragment is unreachable behind a catch-all). Server links stay on the old addresses.
- System status (`/cw/admin/system-status`, `GET /api/v1/system/status`, Settings.Manage + system organisation): `ISystemStatusContributor` per module (`AddSystemStatus<T>`).
- Column filters: *between* sliders with bounds from two `$orderby/$top=1` queries; range facets render their facet view in the column filter.
- Webhooks: `RotateWebhookSecretCommand`, `PreviousProtectedSecret`/`PreviousSecretUntil` (EF change; template migration has no webhooks, apps with webhooks need a migration), `SecretGracePeriod`.
- Template samples: palette command "New product" (`?new=true`) and product search.

## Global search (released in 0.0.1.28)
Docs `website/docs/web/global-search*.md`; changelog *Unreleased*; upgrade rule `0.0.1.28` (removed `AddCommandPaletteEntitySet`).
- Abstractions in `Coworkee.Application/GlobalSearch` (`IGlobalSearchProvider` singletons, own scope per provider and search; `GlobalSearchQuery.Parse` with `"exact"`, `users:max`/`type:users`, GUID ids; `SearchMatch` shared 0..1 scores; `GlobalSearcher` runs providers side by side over a channel, timeout `Coworkee:GlobalSearch:ProviderTimeout`, limit per provider). `IResponseShaper` (Application) is implemented by the response filters module and shapes streamed hits.
- `Coworkee.OData/GlobalSearch`: `AddGlobalSearch<T>(s => s.Fields(...).Page(...))` over `ODataEntityAccess.VisibleAsync` (permission, tenant, row filters; hidden fields refused at resolution), two rounds (prefix/id, then all words contained); endpoints `GET /api/v1/search`, `/stream` (NDJSON, flushed per line, `X-Accel-Buffering: no`), `/providers`; MCP/assistant tool `global_search` (`RunGlobalSearch`).
- Lib providers: users, roles, groups (Identity), files (`/files?file=` opens the preview), reports (`ReportAccess`), chats. `Coworkee.Search` references Application now: `AddGlobalSearchIndex(alias, ...)` (tenant field filter, ES score scaled by the best hit).
- Client: the palette streams server hits after its local providers (`IGlobalSearchApi`, optional service), keeps the chosen entry, *Show all results* (after the first hit) opens library page `search` (`/search?q=&type=`). One new text key in all 22 language files.
- AirWorks: `@coworkee/airworks` registers `globalSearchProvider` in `useCoworkee` (no AirWorks change needed; its `registerSearchProvider` is promise based, so the desktop gets the plain JSON endpoint, not the stream).
- `coworkee module/entity add` emits `AddGlobalSearch<T>` for generated entities with text properties (and the `using`). Demo: brands, products, sales orders, documents. Sharemee: `NodeSearchProvider` (provider `content`, reuses `SearchNodes` with the reader rights, links via `NodeRoutes` in Contracts); people come from the lib's users provider. Sharemee keeps its own `/search` page (app route wins over the library page).
- `GlobalSearcher` resolves `IPermissionChecker` on use (hosts without Identity build).
- Not done: browser check in a running app.

## After 0.0.1.26 (released in 0.0.1.27)
- Fix: the catch-all page of the library swallowed intercepted links to server addresses (setup *Sign in* -> `/bff/login` showed *Not found.*). `LinkNavigation` (scoped, created by `CoworkeeLayout`) records `IsNavigationIntercepted` in a LocationChanging handler; `CoworkeePageHost` force-loads such links. Found by the Sharemee E2E. Worth a 0.0.1.27: every new app of 0.0.1.26 hits it after the setup wizard. App test bases that render the layout without `AddCoworkeeClient` register `LinkNavigation`.
- Flaky: `MailTemplatesPageTests.Add_language_*` (MudEx `ObjectEditForm.HasErrors` enumerates validation messages while bUnit renders on another thread; WASM is single threaded), `ThemesPageTests` on slow runners.
- Demo and Sharemee are on round 7 (migration `Round7`, PWA + push, `InitialAdmin`/realm reconcile, `SyncDevelopmentUsersAsync`; Sharemee dropped `DemoUserTopUp`).

## Round 8 (released in 0.0.1.28)
- Merged: `feat/dashboard-report`, `feat/global-search`, `feat/editors`, `feat/elsa-studio`; one upgrade rule `0.0.1.28` with all breaking changes.
- `WorkflowComponentsTests.Rows_ask_for_their_states_together...` renders both rows in one render pass (the 30 ms batch window of `WorkflowStates` could split two separate renders under load).
- Elsa's `UseWorkflowsApi` (FastEndpoints) set `ValidatorOptions.Global.PropertyNameResolver` to camelCase for every validator of the app (`brand.Name`); the module restores the resolvers (test in `Coworkee.Workflows.Elsa.Tests`).
- MessagePack 2.5.303 referenced by `Coworkee.Aspire` (GHSA-qhrr-8q5h-9q3h on Aspire.Hosting's 2.5.302 broke CI restore).
- Template initial migration: unchanged model (only `cw.DashboardReports`, already in).

## Elsa Studio host (released in 0.0.1.28)
- `Coworkee.Workflows.Elsa.Studio`: Elsa Studio 3.9 as Blazor Server host (`AddCoworkeeElsaStudio`/`MapCoworkeeElsaStudio`), sign-in through Coworkee.Bff (own OIDC client, tokens server side), calls the api's `/api/elsa` with the user's token (`Coworkee:Workflows:Elsa:Studio:BackendUrl`), 403 page without `Workflows.Elsa.Manage`. The host csproj needs `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` (a package props comes too late for restore). `ponytail:` a circuit keeps the token of its connection (reload after expiry).
- Aspire `AddElsaStudio<T>()` (+ `AddProjects` for `*_ElsaStudio`, tool switch `elsastudio`), sets `StudioUrl` of the apis. Template/CLI/Studio option `elsaStudio`. Elsa endpoints are no longer in the OpenAPI documents.
- Demo branch `feat/workflow-sample` (worktree `wt/elsa-studio/demo`): product approval, large-order approval, Elsa workflow *Product approved*, migration `Workflows`, `DemoWorkflows` seed, John = Product Manager, `MyApp.ElsaStudio`. Merged into demo master. Existing demo databases: the role *Product Manager* lacks `Workflows.View/Transition` - `SyncDevelopmentUsersAsync` (lib main after 0.0.1.28) now adds missing roles of seed users and missing permissions of seeded roles, and runs for the migrations of a local Aspire run (`Coworkee:Identity:SyncDevelopmentUsers`, set by `AddMigrations`; the migrations project has no launch profile and ran as Production, so the sync never ran there before). The demo DB fixed itself on the next start.

## Product backlog (older ideas, not part of the recent rounds)
- Own `IMailTransport` with a new provider name is only selectable via `Coworkee:Settings:Defaults:Mail.Provider` (choices are the four built-ins).
- Translations of the 21 new languages were checked by sample, not by native speakers (km, ar least certain).
- MudEx: `MudExDateRangeSlider` Day mode default step ≈ 0 (arrow keys dead; lib passes `StepLength`), no per-thumb aria-label.
- Multi-instance: shared Data Protection key ring needed for pending assistant tool calls; assistant "busy" guard is per instance.
- Not built: MySQL (no EF Core 10 Pomelo), many-to-many in specs, report versioning/public link/map widget, QueryBuilder OData-text import and drag reorder; deploy: `hosting/deployment.md` lists what is verified.
- Flaky under load: Keycloak container start in Aspire tests.
- bUnit tests that find an element and act on it later can hit a stale handler while MudEx `ObjectEdit` re-renders (debounced, 50 ms, also applies `IgnoreIf` conditions a moment after the first render): wrap find and act in `cut.InvokeAsync` and wait for the condition (see `UserAdministrationTests`, `MailTemplatesPageTests`).
