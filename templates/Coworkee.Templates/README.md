# Coworkee.Templates

`dotnet new` template for a complete Coworkee application: Aspire app host, API, OpenIddict auth server, Blazor WebAssembly with BFF, migrations worker and Postgres, SQL Server or SQLite.

```bash
dotnet new install Coworkee.Templates
dotnet new coworkee -n Shop
cd Shop
dotnet run --project src/Shop.AppHost
```

| Option | Default | |
|---|---|---|
| `--samples` | `true` | Catalog and Documents sample modules with pages |
| `--keycloak` | `false` | Keycloak container as external sign-in |
| `--tests` | `true` | API, migration, app host and page tests |
| `--title` | name | Display title of the app |
| `--auditing`, `--notifications`, `--chat`, `--ai`, `--backup` | `true` | Modules of the default app |
| `--files`, `--features`, `--social`, `--workflows`, `--elsa`, `--extendedAttributes`, `--elasticsearch` | `false` | Optional modules (`--elsa` needs `--workflows`) |
| `--eventBus` | `none` | `inProcess` or `rabbitMq` (with a RabbitMQ container) |
| `--clientEntities` | `false` | Browser copies of OData sets |
| `--storage` | `fileSystem` | `azureBlob` with Azurite in the app host |
| `--theme` | `Ocean` | `Coworkee`, `Classic`, `Forest`, `Midnight`, `Sunset`, `HighContrast`, `Octo`, `Code`, `Arctic`, `Vampire`, `Solar`, `Atom`, `Paper`, `Aubergine`, `GitHub`, `LinkHub`, `Terminal`, `Slate`, `Ember`, `Mint` |
| `--primaryColor`, `--secondaryColor` | | Brand colors `#rrggbb`: an app theme as default |
| `--logo` | `coworkee-icon.svg` | File name in `wwwroot` of the web client (copy the file there) |
| `--signInLayout`, `--signInMedia`, `--signInMediaSide` | `Slide`, none, `Left` | Design of the sign-in pages |
| `--loadingScreen` | `Animated` | Background of the loading screen: `Animated`, `SignInMedia` (the sign-in media, with every layout) or `Plain` |
| `--languages`, `--defaultLanguage` | all shipped, `en` | Languages offered from the start (`en,de`) and the default one |
| `--colorMode`, `--registration` | `system`, `false` | Seeded color mode and self registration |
| `--githubActions`, `--devcontainer` | `false` | CI workflow and dev container |

With other modules than the default ones the app has no migration yet: run `coworkee migrations add Initial`. The [Coworkee CLI](https://fgilde.github.io/CoworkeeLib/getting-started/cli/) does that, copies logo and media and adds generated modules with entities (`coworkee new --spec`).

Use a simple name (letters and digits). The generated app references the Coworkee packages of the same version as this template. The seeded users and their generated passwords are in `LOGIN.md` of the solution (from `src/<Name>.Migrations/DemoUsers.cs`).

The [Coworkee CLI](https://www.nuget.org/packages/Coworkee.Cli) (`coworkee new`) wraps this template. Docs: https://fgilde.github.io/CoworkeeLib/
