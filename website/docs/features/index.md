---
title: "Features of the .NET application framework"
description: "What Coworkee brings to a Blazor and ASP.NET Core app: modules, identity, multi-tenancy, data, admin UI, realtime, AI, reporting, localization and Aspire hosting."
---

# Features

Sign-in, a user list, roles, a settings page, an audit log, a table with filters. Coworkee ships these as NuGet packages for ASP.NET Core and Blazor, and one Aspire app host starts them together.

The pages below go through it area by area, with code from the real APIs and screenshots from the demo app (catalog, documents and 100,000 sales orders).

<div class="grid cards" markdown>

-   **[Modular architecture](architecture.md)**

    Modules with `[DependsOn]`, a request pipeline with validation, permissions and caching, the `coworkee` CLI and Coworkee Studio.

-   **[Identity and security](identity.md)**

    Users, roles, groups, permissions down to a single record, an OpenIddict auth server, Keycloak or any OpenID Connect provider, personal API keys.

-   **[Multi-tenancy and settings](multi-tenancy.md)**

    Organisations with separated data, settings per host, organisation and user, features and editions for a SaaS product.

-   **[Data and queries](data.md)**

    EF Core on PostgreSQL, SQL Server or SQLite, field level audit, OData with facets, a query builder and client entities that answer in the browser.

-   **[Blazor admin UI](blazor-ui.md)**

    A MudBlazor shell with navigation, admin pages, data tables, forms generated from the model, theming and three sign-in designs.

-   **[Realtime and collaboration](realtime.md)**

    Live entity changes, notifications, presence, chat, comments and ratings, mail campaigns and background jobs.

-   **[AI assistant and MCP](ai.md)**

    An assistant that works with the user's permissions, 14 model providers and an MCP server in every app.

-   **[Reporting and dashboards](reporting.md)**

    Dashboards built in the browser on top of your OData sets, aggregated by the database in milliseconds.

-   **[Localization](localization.md)**

    23 shipped languages, texts edited at runtime and machine translation through LibreTranslate, DeepL and others.

-   **[Aspire and observability](observability.md)**

    One app host for database, Redis, Keycloak, Grafana with logs, metrics and traces, WebDataStudio and AspireUI.

-   **[Files and search](files.md)**

    A file manager with previews and public links on top of blob storage, plus full text search.

-   **[Use cases](use-cases.md)**

    Back office, B2B SaaS, customer portal and Sharemee, the asset platform this framework was extracted for.

</div>

Is a framework worth it at all, compared with a plain ASP.NET Core template? [Why Coworkee](why-coworkee.md) puts the two side by side, including where the template wins.
