# COWORKEE_APP_TITLE

Built on [Coworkee](https://fgilde.github.io/CoworkeeLib/). Needs the .NET SDK from `global.json` and Docker.

```bash
coworkee run                      # or: dotnet run --project src/MyApp.AppHost
coworkee migrations add <Name>    # after model changes
coworkee module add <Name>        # new feature module
coworkee update                   # newest Coworkee packages
```

Sign in as `admin@myapp.local`; the generated password is in `src/MyApp.Migrations/DemoSeed.cs`. Change it before you deploy anywhere.

| Project | |
|---|---|
| `MyApp.AppHost` | Aspire topology: Postgres, Redis, Mailpit, auth server, API, web, migrations |
| `MyApp.Api` | API host |
| `MyApp.Auth` | OpenIddict auth server |
| `MyApp.Web`, `MyApp.Web.Client` | Blazor WebAssembly with backend for frontend |
| `MyApp.Infrastructure` | DbContext, migrations, module composition |
| `MyApp.Migrations` | Applies migrations and the seed before the hosts start |
| `MyApp.Contracts` | DTOs, permission names, texts |
