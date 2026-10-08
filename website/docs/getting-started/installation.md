# Installation

## Prerequisites

- .NET SDK 10
- Docker (Postgres, Redis, Mailpit and Keycloak run as containers)
- An IDE with Aspire support, or just the `dotnet` CLI

## Start from the template

The template is a complete application: catalog, documents, dashboard, administration, seeded users and Keycloak.

```bash
git clone https://github.com/fgilde/CleanArchitectureBaseBlazor myapp
cd myapp
dotnet run --project src/MyApp.AppHost
```

The console prints the URL of the Aspire dashboard. The app itself runs on `https://localhost:7300`.

![The home page of the template](../assets/screenshots/home.png){ .shot }

## Sign in

The migration service applies the migrations and seeds the demo data before the other services start. There is no setup wizard in the template.

| User | Roles | Notes |
|---|---|---|
| `info@coworkee.de` | Administrator | also exists in Keycloak |
| `fgilde@gmail.com` | Administrator | |
| `john@coworkee.de` | none | sees only what you grant |

The passwords are in `src/MyApp.Migrations/DemoSeed.cs`. Change them, or replace the seed with your own users, before the app leaves your machine.

The sign-in page also offers **Sign in with Keycloak**. The Keycloak user shares the address of the administrator, so the first Keycloak sign-in links both accounts. Its password is a generated Aspire parameter: open the dashboard, resource `myapp-keycloak-user-password`.

![Sign-in page with Keycloak](../assets/screenshots/login.png){ .shot }

## Packages for an existing app

All packages start with `Coworkee.`. Pick the ones your services need, the module system pulls in their dependencies:

```xml
<ItemGroup>
  <PackageReference Include="Coworkee.AspNetCore" />
  <PackageReference Include="Coworkee.Infrastructure" />
  <PackageReference Include="Coworkee.Identity" />
  <PackageReference Include="Coworkee.OData" />
</ItemGroup>
```

[Packages and namespaces](../reference/packages.md) lists what each package contains.

!!! note "Building against the sources"
    While the packages are not on nuget.org, `build/pack-local.ps1` in the CoworkeeLib repository packs them into `artifacts/nuget`, and the template's `nuget.config` reads from there.

## Next

[Solution structure](solution-structure.md) explains the projects of the template, [Your first feature](first-feature.md) adds a new entity end to end.
