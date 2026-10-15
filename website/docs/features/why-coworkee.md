---
title: "Why use a .NET application framework?"
description: "Coworkee compared with a hand-rolled ASP.NET Core project and a copied project template: what you build yourself, what you maintain, and where each one wins."
---

# Why Coworkee

You can write the shared parts of an ASP.NET Core and Blazor app yourself, copy them from a project template, or take them as packages. Coworkee is the third way, and it grew out of the second: it started as a Blazor template of mine that I copied into every new app, until keeping the copies in step took more time than the apps.

## Side by side

| | Hand-rolled ASP.NET Core | Copied project template | Coworkee packages |
|---|---|---|---|
| Sign-in, users, roles, permissions | you write and secure it | in the copy, frozen on the day you copied | [packages](identity.md), updated with the package version |
| Multi-tenancy | query filters and tenant resolution by hand | depends on the template | [tenants, features and editions](multi-tenancy.md) |
| Admin pages | you build each one | in the copy, yours to maintain | users, roles, settings, themes, languages, mail, audit, backups and more |
| Tables with filters and export | per entity | one generic table, if any | [facets, column filters, grouping, views, export](blazor-ui.md) over OData |
| Local environment | compose files you keep in step | whatever was there when you copied | one [Aspire app host](observability.md) |
| Fixes in shared code | yours | carried into every copy by hand | the next package version |
| Control over every line | full | full, after the copy | through [replaceable components](../ui/customizing.md) and interfaces, not everywhere |
| Learning curve | none beyond ASP.NET Core | the template's own conventions | modules, the dispatcher, the settings model |
| Runtime | any, including .NET 8 LTS | whatever the template targets | .NET 10 only |
| Dependency risk | none | none after the copy | a 0.0.x project with one maintainer |

The last four rows go to the hand-rolled project. If you need .NET 8 LTS, or your team has no time to learn another set of conventions, start from scratch.

## What you get in return

The demo app shows it best. Its own code is a product catalog, documents and sales orders. Everything else on the screenshots on this site, from the sign-in page to the audit log, comes out of the packages. Sharemee, the second app on the library, is in the same position: its projects are about assets and metadata, not about user administration.

A template is a snapshot. When I fix the sign-in flow, add a language or make the data table faster, every app on Coworkee gets it with the next version, and the breaking changes are listed in the [changelog](../reference/changelog.md). About 1,500 automated tests run against PostgreSQL, SQL Server and SQLite, and the CLI's template tests build real generated apps.

Every request goes through the same pipeline (validation, permission check, caching, unit of work). Once a new developer has seen it in one module, they have seen it in all of them.

## What it costs

- An opinionated stack: EF Core, MudBlazor with MudBlazor.Extensions, OpenIddict, FluentValidation, Aspire. Another UI library or ORM means working against the framework.
- .NET 10 only.
- Conventions to learn. The [first feature](../getting-started/first-feature.md) walkthrough shows them on one entity.
- A young API. The packages are on 0.0.1.x and things still move between releases; pin versions and read the changelog before you update.

## Where you can step around it

Every Blazor component of the library can be swapped through `ReplaceComponent`. Modules are optional: leave out chat or reporting and their tables, endpoints and pages stay out of the app. Your endpoints are plain minimal APIs, so a hand-written one next to the generated ones is fine. Mail transports, machine translation, blob storage and search sit behind interfaces you can implement yourself.

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop && coworkee run
```

Or `coworkee ui` for the desktop wizard. The [use cases](use-cases.md) show what the framework is made for.
