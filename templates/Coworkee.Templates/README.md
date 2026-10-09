# Coworkee.Templates

`dotnet new` template for a complete Coworkee application: Aspire app host, API, OpenIddict auth server, Blazor WebAssembly with BFF, migrations worker and Postgres.

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

Use a simple name (letters and digits). The generated app references the Coworkee packages of the same version as this template. The seeded administrator and its generated password are in `src/<Name>.Migrations/DemoSeed.cs`.

The [Coworkee CLI](https://www.nuget.org/packages/Coworkee.Cli) (`coworkee new`) wraps this template. Docs: https://fgilde.github.io/CoworkeeLib/
