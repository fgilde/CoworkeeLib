# Plan: AirWorks client (spec 2026-10-14-airworks-client-design.md)

Each task ends with its tests green and a commit on `feat/airworks`.

1. **About fixes** – `AboutCredit.IconBackground`, `AboutCredit.AirWorks()`, Nextended version via `AssemblyMetadata`, no URL text in credit tiles. Tests: `AboutTests`.
2. **BFF cookie name** – `BffOptions.CookieName`, applied in `AddCoworkeeBff`. Test in `Coworkee.Bff.Tests`.
3. **Aspire `AddAirWorks`** – shared BFF client wiring with `AddWeb`, client slot counter, cookie name, `AddProjects` role `_AirWorks`. Tests in `Coworkee.Aspire.Tests`.
4. **`@coworkee/client`** – workspace `packages/`, snapshot `openapi.json`, generated `schema.ts`, client, bff, errors, theme, texts, odata. `node --test`.
5. **`@coworkee/airworks`** – `useCoworkee`, auth provider, brand/theme, texts, apps (settings, profile, files + MudEx preview, data, entityApp, notifications, api docs), styles. `node --test` for pure parts; `tsc` build.
6. **Localization** – new keys in the 22 library languages; `build/localization-check.ps1` for each.
7. **Template** – `MyApp.AirWorks` project, `airworks` symbol, slnx/AppHost/csproj/Directory.Packages/LOGIN.md/web client credit, `.ts` conditionals, tarballs packed by `Coworkee.Templates.csproj`.
8. **CLI + Studio** – option `airworks` with `InfoUrl`, `coworkee options` prints it, Studio card link + summary project. Tests: Cli `OptionTests`/`TemplateOptionTests` (content + build), Studio `OptionTests`.
9. **Docs** – `website/docs/clients/airworks.md` + `.de.md`, CLI/Studio/Aspire pages, nav; `mkdocs build --strict`.
10. **Verification** – pack-local, CLI template tests with the feed, generated app in Aspire with HTTP checks; report `docs/superpowers/airworks-report.md`; CONTINUE.md section.
