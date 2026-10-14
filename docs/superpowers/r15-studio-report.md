# R15 Studio: option dependencies, icons and links, explicit modules

Branch `feat/studio-cards` (from main), worktree `wt\r15-studio\coworkee`. Not pushed.

## 1. Options that need another one

- `CliOption.DependsOn` (`OptionDependency(option, values...)`, no values = "the other option is set / a bool is true"); `OptionCatalog.Unmet/IsActive` follow chains (`signInMediaSide` > `signInMedia` > `signInLayout`).
- Catalog audit, everything that only makes sense with another option:

| Option | Needs |
|---|---|
| `airworksPackages` | `airworks` |
| `aiProvider`, `aiModel`, `aiEndpoint` | `ai` |
| `backup` | `database = postgres` (the template ignored it already; `--backup true` off Postgres stays an error) |
| `signInMedia` | `signInLayout = Split or Slide` (the Card page draws no media) |
| `signInMediaSide` | `signInMedia` |

  Not applicable: there are no WebDataStudio/AspireUI sign-on variants, Keycloak-related or Redis-dependent options in the catalog (they follow Keycloak automatically in the template). `translationSource` stays independent: the app host sets `TranslationSource` regardless of the provider.
- CLI: there are no prompts (every value is a switch or a spec key), so "not asked" means: an unmet option is not passed to `dotnet new` (`NewCommand.TemplateArgs`), its sign-in media is not copied (`Assets`), and `coworkee new`, `coworkee spec validate` and MCP `validate_spec` print `warn  airworksPackages: ignored, it only matters with airworks (coworkee options).` (`SpecValidator.Warnings`). `coworkee options` prints `Needs:`, `--json` has `dependsOn`, switch help and the spec schema say "Only used with ...".
- Studio: `OptionViewModel.IsRelevant` (set in `MainViewModel.SpecChanged`), `OptionGroupViewModel.Shown` (cards appear/disappear at their place), the AI section is hidden without the assistant, `ToSpec` and `CommandLine.Flags` leave unmet options out; the value is kept when the other option is switched on again. The old backup "greyed out + note" is replaced by hiding (the Database card note already says it).

## 2. Icons and links

- Every option has a Material Design icon (`Model/OptionLooks.cs`, 32 new geometries in `Styles/Icons.axaml`; a test checks that every catalog option has an existing icon). Cards show it in a rounded square next to the title; infrastructure cards keep the icon of the chosen choice.
- `InfoUrl` on options: AirWorks, AspireUI, WebDataStudio, Grafana (with Prometheus/Loki/Tempo, which are no options of their own), Keycloak, Redis, Elasticsearch, GitHub repository, git, GitHub Actions, dev container. New `OptionChoice.InfoUrl`: PostgreSQL, SQL Server, SQLite, RabbitMQ, Azure Blob Storage, Mapperly, npm, Ollama, LM Studio, LibreTranslate, MyMemory, DeepL, Azure AI Translator, Google Cloud Translation (Mailpit is not an option).
- Studio: an open button (Material "open in new") on the card header opens the link of the option, or of the chosen choice (tooltip "More about PostgreSQL"); replaces the old text link. CLI: `coworkee options` prints `More:` and `More (choice):`, `--json` has `infoUrl` on option and choice.

## 3. "All modules on, the spec shows no modules"

Reproduced: with the Everything preset (all library modules on) the Studio spec had `options` with only the modules that differ from the defaults (`files`, `features`, ...), and with the defaults `"options": {}` and `"modules": []`. Cause: a spec lists only deviations from the defaults (intentional for CLI/hand-written specs: smaller, and new defaults apply), and `modules` means *your own* modules with entities, not the library modules, which are options. The owner's expectation is right for the final summary, so:

- Studio spec (preview, save, `new --spec`): every library module is written with `true`/`false` (`MainViewModel.ListModules`, effective values incl. `clientEntities` switched on by an entity, no `backup` off Postgres), in catalog order. Caption on the review page explains options vs. modules.
- CLI: new `coworkee spec show <file>` (`SpecSummary`): "Library modules on: ... / off: ...", every other option with its effective value (`(unused)` where ignored), own modules. Spec schema description of `modules` says what it is.
- Tests: Studio `With_every_module_on_the_spec_at_the_end_names_them_all`, `Only_changed_options_go_into_the_spec_but_every_module_is_listed`, client entity case; CLI `SpecSummary` tests with defaults and all on.

## 4. Tests and evidence

- `dotnet build Coworkee.slnx -c Release`: 0 errors.
- `tests/Coworkee.Cli.Tests`: 193 passed, 10 skipped (real app builds need `COWORKEE_TEST_FEED`; template content is unchanged, so not run). New: `OptionDependencyTests` (11 facts: dependency table, AirWorks, AI/backup, sign-in chain, options text/JSON links, summary on/off, `spec show`/`validate`, schema); `Each_switch_reaches_the_template...` adds the needed switches.
- `tests/Coworkee.Studio.Tests`: 282 passed, 1 skipped (E2E needs a feed). New: all-modules spec, explicit modules, client entity, AirWorks packages hidden/shown/not written, dependent options follow, icons exist, links per card and per choice. Screenshots rendered headless (architecture page checked: icons, open buttons, AirWorks packages card hidden).
- Docs EN + DE: `getting-started/cli`, `studio`, `mcp`, `reference/changelog`; `mkdocs build --strict` ok.

## Left out on purpose

- No real app build against a packed feed (no template change). The CLI path was covered through `TemplateArgs`.
- Spec files that set an unmet option (e.g. `signInMedia` with layout `Card`) now produce an app without that media (warning); before the media was copied and set although the Card page does not show it.
