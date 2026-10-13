# CONTINUE – handover for the next agent

State of the Coworkee library as of 2026-10-12, release **0.0.1.20** (main `e4c6665`, CI green on Postgres, SQL Server and SQLite).

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

## What exists (short)
Modules (identity, OpenIddict auth server with Card/Split/Slide sign-in, settings, theming (Ocean default), localization + machine translation (LibreTranslate default, MyMemory, DeepL, Azure, Google, AI), realtime incl. presence/active role, chat, files, reporting (GridStack), facets + generic QueryBuilder, client entities, AI (14 providers, admin page `/admin/ai-settings`), MCP server `/mcp`, personal API keys, assistant window), DB providers Postgres/SQL Server/SQLite, optional Redis, Aspire integration (Keycloak with realm reconcile, Grafana, WebDataStudio, LibreTranslate, Ollama), CLI `coworkee` (new/spec/options/module/entity/ui/mcp/self-update), desktop wizard Coworkee Studio (Avalonia, `coworkee-ui`), website with landing page.

## Owner issues 1-3 (fixed, branches not merged yet)
1. **VS debug of a generated app** ("Unable to launch browser: Could not open wss://…/_framework/debug/ws-proxy"): the web host lacked `app.UseWebAssemblyDebugging()` in Development, so the `inspectUri` of the web launch profile hit a 404. Template and demo fixed (branch `fix/debug-sqlite-aspireui`, demo commit on master); verified with a generated app: CDP through `/_framework/debug/ws-proxy` works. Sharemee's `Shareme.Web/Program.cs` has the same gap.
2. **WebDataStudio + SQLite `disk I/O error`**: EF Core creates SQLite databases in WAL mode, and WAL's shared-memory index does not work across the Docker Desktop bind mount. With `UseWebDataStudio` the app host now switches/creates the file with `journal_mode=DELETE` before the app starts (EF leaves existing files alone). Verified: generated sqlite app, studio browses `Users`.
3. **AspireUI + Keycloak**: `aspireUi.WithCoworkeeSignOn(app)` (branch `feat/aspireui-signon`, docs in hosting/aspire.md) registers `{app}-{resource}` with `/api/auth/sso/callback`, uses the container authority + `KC_HOSTNAME`, `roles` as groups, `Administrator` as admin group and forces the seeded settings. Works with released Nextended 10.1.44; verified (Keycloak sign-in, admin). The owner's own volume `aspireui-data-aspireui` holds `OidcAuthority={keycloak.bindings.http.url}/realms/gildestore` (a ReferenceExpression passed as string; AspireUI seeds only empty settings) - clear it or use `WithCoworkeeSignOn`. Nextended main has a new `WithSingleSignOn(ReferenceExpression authority, …)` overload (unreleased, not needed by the lib).
   - Later: `options.UseAspireUI()` like Grafana/WebDataStudio (own volume per app).

## Owner round 2026-10-13 (all on main, not released yet)
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

## Known follow-ups
- After the MudEx release with `feat/coworkee-p4`: lib `[Cron]` → `MudExCronEdit`, delete `CronEditor`/`CronSchedule`; `SettingsModel` two `WithAttributesIf` → `AsDisabledIf`; drop the `WrapInColumns`/`RenderAsChips` re-wraps after `RenderWith`.
- Own `IMailTransport` with a new provider name is only selectable via `Coworkee:Settings:Defaults:Mail.Provider` (choices are the four built-ins).
- Translations of the 21 new languages were checked by sample, not by native speakers (km, ar least certain).
- MudEx 9.11.2: `MudExDateRangeSlider` Day mode default step ≈ 0 (arrow keys dead; lib passes `StepLength`), no per-thumb aria-label. MudEx dialog conditions fix is local branch `fix/passed-meta-conditions-in-form` (ccc2486b); after a MudEx release remove `.UpdateAllConditionalSettings()` in `EditDialogExtensions`.
- Multi-instance: shared Data Protection key ring needed for pending assistant tool calls; assistant "busy" guard is per instance.
- Lib pages live under generic `/admin/...` routes; an app page with the same route breaks Blazor routing (`AppShellTests` covers `/admin/ai`). Consider a prefix.
- Not built: MySQL (no EF Core 10 Pomelo), many-to-many in specs, report versioning/public link/map widget, QueryBuilder OData-text import and drag reorder, deploy story (aspire publish) not verified.
- Flaky under load: Keycloak container start in Aspire tests.
