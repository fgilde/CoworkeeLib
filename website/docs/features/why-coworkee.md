---
title: "Why use a .NET application framework?"
description: "Why build on Coworkee: updatable NuGet packages, modules you pick, security, tenants and observability built in, a base for AI agents, CLI and Studio, on an open .NET 10 stack."
---

# Why Coworkee

Every business app needs the same foundation before its first feature: sign-in, users and roles, permissions, tenants, settings, an admin UI, audit, mail, a local environment and a way to deploy. Coworkee ships that foundation as NuGet packages for ASP.NET Core, Blazor and Aspire, so the code in your solution is your domain.

## Packages you update, not code you inherit

Everything ships as packages starting with `Coworkee.`, pinned by one `<CoworkeeVersion>` in `Directory.Packages.props`. A fix in the sign-in flow, a new language or a faster data table reaches every app with the next version.

`coworkee update` sets that version and applies the upgrade rules the CLI ships for every release: it prints the breaking changes between your version and the new one, applies the ones it can (renames, package and SDK versions, template lines) and lists the manual steps with the `file:line` they concern. `--dry-run` shows the plan and changes nothing. The [changelog](../reference/changelog.md) lists the same changes per version.

## Modular: take what you need

Each area is a module in its own package: identity, settings, mailing, files, search, realtime, notifications, auditing, workflows, reporting, the AI assistant, chat, webhooks, backups and more. Leave a module out and its tables, endpoints and pages stay out of the app. `coworkee add` and `coworkee remove` switch options on an existing app later, and Coworkee Studio starts from presets (minimal, business app, collaboration, SaaS platform and others) with the trade-off of each choice next to it. See [Modular architecture](architecture.md).

## Production concerns built in

- **Security.** An OpenIddict auth server with Keycloak or any OpenID Connect provider, tokens kept on the server behind a session cookie, permissions down to a single resource, personal [API keys](../security/api-keys.md), rate limits and a Content-Security-Policy on the sign-in pages. See [Identity and security](identity.md).
- **Multi-tenancy.** `IMultiTenant` on an entity filters every query by organisation; features and editions turn an app into a SaaS product. See [Multi-tenancy](multi-tenancy.md).
- **Audit and history.** Field-level audit in the same transaction as the change, record history with restore and undo.
- **Observability.** One [Aspire app host](observability.md) starts the app with PostgreSQL or SQL Server, Redis, Mailpit and, when you want them, Grafana with Prometheus, Loki and Tempo. `coworkee deploy` publishes the same app host for Docker Compose, Azure Container Apps or Kubernetes.
- **Testing.** `Coworkee.Testing` runs API tests against a real PostgreSQL, SQL Server or SQLite database and acts as any user; the generated solution comes with test projects for the API, the app host, the migrations and the Blazor client. The library itself has more than 1,500 tests; CI runs the persistence-heavy suites on all three databases. See [Testing](../testing.md).

## A base for building with AI

Because security, tenants, audit, realtime and the admin UI come with the packages, a coding agent only has to write the domain, and what it writes plugs into code that already checks permissions and filters by tenant. The CLI is an MCP server (`coworkee mcp`), a whole app is a JSON spec with a published schema, and every running app is an MCP server for its own data. See [Building with AI](building-with-ai.md).

## Tooling

- **CLI.** `coworkee new`, `run`, `module add`, `entity add`, `entity import` (from a database, OpenAPI or CSV), `migrations add`, `update`, `deploy`, `doctor`. See [Command line](../getting-started/cli.md).
- **Coworkee Studio.** A desktop wizard for Windows, macOS and Linux: name, logo and theme, sign-in design, modules and infrastructure, entities, then it creates and runs the app. See [Coworkee Studio](../getting-started/studio.md).
- **AirWorks client.** `coworkee new --airworks` adds a web desktop with windows and a taskbar next to the Blazor app, on the same auth server and API. See [AirWorks client](../web/airworks.md).

## An open .NET 10 stack

.NET 10, ASP.NET Core minimal APIs, Blazor with MudBlazor and MudBlazor.Extensions, EF Core on PostgreSQL, SQL Server or SQLite, OpenIddict, FluentValidation and Aspire. MIT licensed, with the source on [GitHub](https://github.com/fgilde/CoworkeeLib).

Your own code stays plain: endpoints are minimal APIs, so a hand-written one next to the generated ones is fine. Every Blazor component of the library can be swapped through [`ReplaceComponent`](../ui/customizing.md), and mail transports, machine translation, blob storage and search sit behind interfaces you can implement yourself.

## Good to know

- Coworkee targets .NET 10 only.
- The stack is chosen: another UI library or ORM means working against it.
- The packages are on 0.0.1.x and the API still moves between releases. Pin the version and run `coworkee update --dry-run` before you update.
- The conventions take a moment to learn. [Your first feature](../getting-started/first-feature.md) shows them on one entity.

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop
cd Shop && coworkee run
```

Or `coworkee ui` for the desktop wizard. The [use cases](use-cases.md) show what the framework is made for.
