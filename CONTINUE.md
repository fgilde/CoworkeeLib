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

## Open issues reported by the owner (not started)
1. **VS debug of a generated app stops** with "Failed to launch debug adapter … Unable to launch browser: Could not open wss://localhost:<port>/_framework/debug/ws-proxy?browser=…". Blazor WASM debugging through the AppHost. Check the generated `src/*.Web/Properties/launchSettings.json` (`inspectUri`, `launchBrowser`) and the AppHost launch profile; compare with the demo, which debugs fine (verify). Likely: `inspectUri` on the BFF host profile while VS launches the browser for the AppHost, or the WASM debug proxy path not forwarded by the BFF. Reproduce with a Studio-generated app.
2. **WebDataStudio cannot read the SQLite app database**: `SQLite Error 10: 'disk I/O error'`. The lib mounts the app's `.data/<name>.db` into the WebDataStudio container (`src/Coworkee.Aspire/CoworkeeApp.WebDataStudio.cs`). SQLite locking over a Docker Desktop bind mount (Windows host) fails while the app holds the file (WAL). Options: read-only/immutable open (`file:...?mode=ro&immutable=1` if the studio's driver accepts URI filenames), mount the folder instead of the file, or switch the app to `journal_mode=DELETE` in dev. WebDataStudio itself reads SQLite fine when the owner adds a local file manually.
3. **AspireUI with the app's Keycloak** (owner adds AspireUI manually in his app host, not in the lib yet). Use the lib's Keycloak API (call after `UseKeycloak` + `AddAuthServer`, see `website/docs/hosting/aspire.md` → Keycloak):
   ```csharp
   var aspireUi = builder.AddAspireUI()
       .WithAdminUser("admin", "admin")
       .WithProjectStack("GildeStore", false, resourceBuilders);
   var sso = app.AddKeycloakClient("aspireui", [ReferenceExpression.Create($"{aspireUi.GetEndpoint("http")}/signin-oidc")]); // callback path of AspireUI: check its docs
   aspireUi.WithSingleSignOn(authority: app.KeycloakAuthority, clientId: sso.ClientId, clientSecret: sso.Secret,
       label: "Keycloak", groupsClaim: "roles", adminGroup: "Administrator", defaultPermissions: AspireUIPermissions.AppUser);
   ```
   - The realm puts the users' roles into the `roles` claim; admins from `DemoUsers.cs` get `Administrator`.
   - `KeycloakAuthority` is a `ReferenceExpression` (dynamic port). If `WithSingleSignOn` only takes a string, add an overload in `Nextended.Aspire.Hosting.AspireUI` (Nextended repo) taking `ReferenceExpression`/`IResourceBuilder<ParameterResource>`.
   - The containers must reach Keycloak under the same issuer the browser sees: the lib sets `KC_HOSTNAME` only when a tool uses Keycloak (`CoworkeeApp.Tools.cs`); make AspireUI count as such a tool.
   - Later: move this into the lib as `options.UseAspireUI()` like Grafana/WebDataStudio.

## Known follow-ups
- MudEx 9.11.2: `MudExDateRangeSlider` Day mode default step ≈ 0 (arrow keys dead; lib passes `StepLength`), no per-thumb aria-label. MudEx dialog conditions fix is local branch `fix/passed-meta-conditions-in-form` (ccc2486b); after a MudEx release remove `.UpdateAllConditionalSettings()` in `EditDialogExtensions`.
- Multi-instance: shared Data Protection key ring needed for pending assistant tool calls; assistant "busy" guard is per instance.
- Lib pages live under generic `/admin/...` routes; an app page with the same route breaks Blazor routing (`AppShellTests` covers `/admin/ai`). Consider a prefix.
- Not built: MySQL (no EF Core 10 Pomelo), many-to-many in specs, report versioning/public link/map widget, QueryBuilder OData-text import and drag reorder, deploy story (aspire publish) not verified.
- Flaky under load: Keycloak container start in Aspire tests, `LiveUpdatesTests` avatar test.
