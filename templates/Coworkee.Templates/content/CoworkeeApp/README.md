# COWORKEE_APP_TITLE

<!--#if (hasRepository) -->
[![QuickRun](https://quickrun.org/badge.svg)](https://quickrun.org/run?repo=COWORKEE_REPOSITORY)

<!--#endif -->
Built on [Coworkee](https://fgilde.github.io/CoworkeeLib/). Needs the .NET SDK from `global.json` and Docker.

```bash
coworkee run                      # or: dotnet run --project src/MyApp.AppHost
coworkee migrations add <Name>    # after model changes
coworkee module add <Name>        # new feature module
coworkee update                   # newest Coworkee packages
```

## QuickRun

[QuickRun](https://quickrun.org) starts the app from `quickrun.yml` with one click: it checks the .NET SDK and Docker, trusts the development certificate, runs the app host and opens the web app once it answers.

## The solution

<!--#if (!defaultModel) -->
The chosen modules or database differ from the shipped initial migration (Postgres), so the solution has none yet. `coworkee new` creates it; otherwise run `coworkee migrations add Initial` once before the first start.

<!--#endif -->
How to sign in, with the users and their generated passwords: [LOGIN.md](LOGIN.md). Change the passwords before you deploy anywhere.

| Project | |
|---|---|
| `MyApp.AppHost` | Aspire topology: database (Postgres, SQL Server or SQLite), Redis, Mailpit, auth server, API, web, migrations |
| `MyApp.Api` | API host |
| `MyApp.Auth` | OpenIddict auth server |
| `MyApp.Web`, `MyApp.Web.Client` | Blazor WebAssembly with backend for frontend |
<!--#if (airworks) -->
| `MyApp.AirWorks` | [AirWorks](https://fgilde.github.io/AirWorks/) web desktop on the same api (TypeScript in `ClientApp`, built with Node.js) |
<!--#endif -->
| `MyApp.Infrastructure` | DbContext, migrations, module composition |
| `MyApp.Migrations` | Applies migrations and the seed before the hosts start |
| `MyApp.Contracts` | DTOs, permission names, texts |
