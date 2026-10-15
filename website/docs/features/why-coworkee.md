---
title: "Why use a .NET application framework?"
description: "Coworkee compared with a hand-rolled ASP.NET Core project and a copied project template: what you build yourself, what you maintain, and the trade-offs."
---

# Why Coworkee: framework, template or start from scratch

There are three common ways to start a business application on ASP.NET Core and Blazor. You write the shared parts yourself. You copy a project template that already has them. Or you take them as packages from a framework and keep only your domain in the solution. Coworkee is the third option, and it grew out of the second: it started as a Blazor template of mine that I copied into every new app, until keeping the copies in step cost more than the apps themselves.

This page compares the three without pretending the framework has no cost.

## Side by side

| | Hand-rolled ASP.NET Core | Copied project template | Coworkee packages |
|---|---|---|---|
| Sign-in, users, roles, permissions | you write and secure it | in the copy, frozen at the day you copied | [packages](identity.md), updated with a version bump |
| Auth server and external providers | you integrate OpenIddict, Keycloak or another IdP | often only one of them | OpenIddict auth server plus Keycloak or any OIDC provider, sign-in designs included |
| Multi-tenancy | query filters and tenant resolution by hand | depends on the template | [tenants, features and editions](multi-tenancy.md) |
| Admin UI | you build each page | pages in the copy, yours to maintain | users, roles, settings, themes, languages, mail, audit, backups, AI and more out of the box |
| Data tables with filters and export | per entity, or a grid library you wire up | one generic table, if any | [facets, column filters, grouping, views, export and import](blazor-ui.md) over OData for every entity |
| Audit trail | interceptors you write | maybe | [field level, with a timeline per record](data.md) |
| Realtime | SignalR hubs you design | rarely | [entity changes, notifications, presence, chat](realtime.md) |
| Local environment | docker compose files you keep in step | whatever was there when you copied | one [Aspire app host](observability.md) with database, Redis, Mailpit, Keycloak, Grafana |
| Security fixes and improvements | yours | carried into every copy by hand | `dotnet add package` / a version bump |
| Time to the first domain feature | weeks | days, then drift | `coworkee new` and an entity |

## What you gain

**Less code that is not your product.** In the demo app, the code in the solution is the catalog, documents and sales orders. Everything else on the screenshots comes from packages. The same holds for Sharemee, a digital asset platform: its own projects are about assets, metadata and processing.

**Updates instead of drift.** A template is a snapshot. A fix in the sign-in flow, a new language or a faster query in the data table reaches every app built on Coworkee with the next package version. Breaking changes are listed in the [changelog](../reference/changelog.md).

**Decisions already made and tested.** About 1,500 automated tests run against PostgreSQL, SQL Server and SQLite. The CLI's template tests build real generated apps. You still own your domain tests, but not the tests for password rules, permission checks or tenant filters.

**One way to do things.** Every request goes through the same pipeline with validation, permission checks, caching and a unit of work. New developers learn it once and find it in every module.

## What it costs

**Coworkee is opinionated.** It uses EF Core, MudBlazor and MudBlazor.Extensions, OpenIddict, FluentValidation and Aspire. If your team has settled on a different UI library or ORM, you will fight the framework rather than use it.

**It targets .NET 10.** Apps that must stay on an older runtime cannot use it.

**You learn its conventions.** Modules, `[DependsOn]`, the dispatcher and the settings model are concepts to learn before you are fast. The [first feature](../getting-started/first-feature.md) walkthrough shows all of them on one entity.

**It is a young project.** The packages are on version 0.0.1.x and the API still moves between releases. Pin versions and read the changelog before you update.

## Escape hatches

A framework is only acceptable if you can step around it where it does not fit:

- Every Blazor component of the library can be [replaced](../ui/customizing.md) with your own through `ReplaceComponent`.
- Modules are optional. Leave out chat, reporting or AI and their tables, endpoints and pages stay out of the app.
- Your endpoints are plain ASP.NET Core minimal APIs; nothing stops you from writing one by hand next to the generated ones.
- Mail transports, translation providers, blob storage and search are behind interfaces you can implement.

## Try it

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop && coworkee run
```

Or start `coworkee ui` for the desktop wizard. The [installation guide](../getting-started/installation.md) explains each step, and the [use cases](use-cases.md) show what kinds of apps the framework is made for.
