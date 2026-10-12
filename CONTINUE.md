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

## Known follow-ups
- MudEx 9.11.2: `MudExDateRangeSlider` Day mode default step ≈ 0 (arrow keys dead; lib passes `StepLength`), no per-thumb aria-label. MudEx dialog conditions fix is local branch `fix/passed-meta-conditions-in-form` (ccc2486b); after a MudEx release remove `.UpdateAllConditionalSettings()` in `EditDialogExtensions`.
- Multi-instance: shared Data Protection key ring needed for pending assistant tool calls; assistant "busy" guard is per instance.
- Lib pages live under generic `/admin/...` routes; an app page with the same route breaks Blazor routing (`AppShellTests` covers `/admin/ai`). Consider a prefix.
- Not built: MySQL (no EF Core 10 Pomelo), many-to-many in specs, report versioning/public link/map widget, QueryBuilder OData-text import and drag reorder, deploy story (aspire publish) not verified.
- Flaky under load: Keycloak container start in Aspire tests, `LiveUpdatesTests` avatar test.
