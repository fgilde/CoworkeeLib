---
title: ".NET Aspire hosting with Grafana and dev tools"
description: "One AddCoworkeeApp call wires databases, Redis, Mailpit, Elasticsearch and Keycloak in the .NET Aspire app host, with Grafana dashboards and a database browser."
---

# Developer tooling and hosting on .NET Aspire

Before a business app does anything useful, someone has to start a database, a cache, a mail server for the password reset, maybe a search engine and an identity provider, and then copy connection strings into four `appsettings.json` files. When something is slow, the next question is where to look: logs in one console, traces in another, the database in a third tool.

Coworkee builds on the .NET Aspire app host and does that wiring for you. You name the app; Coworkee finds its services, reads which packages each one uses and adds the containers and settings they need. Grafana with metrics, logs and traces and a browser-based database studio come along in development, both signed in with the app's own administrator.

![Grafana overview dashboard with requests per second, errors, latency and log volume per service](../assets/screenshots/grafana-overview-dark.webp){ .shot loading=lazy }

## One call in the Aspire app host

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.UseGrafana();
    options.UseWebDataStudio();
}).AddProjects();

builder.Build().Run();
```

`AddProjects()` adds the projects the app host references by naming convention: `*_Migrations`, `*_Auth`, `*_Api`, `*_Worker`, `*_Web`. Each gets its role: the API learns its token authority, the web app gets its OIDC client at the auth server, the migrations run before anything that needs the database.

## Infrastructure from the packages a service uses

Coworkee looks at the packages of each service, directly or through other projects, and adds only what is needed:

| Package in the service | It gets |
|---|---|
| `Coworkee.Infrastructure` | the app database, and waits until the migrations finished |
| `Coworkee.Realtime` | Redis as SignalR backplane and for cache invalidation between instances |
| `Coworkee.Mailing` | Mailpit as local SMTP server with a web inbox |
| `Coworkee.Storage` | a shared blob folder `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |

The database is PostgreSQL by default. `UseSqlServer()` swaps in a SQL Server container and `UseSqlite()` a file next to `src`, without any container; both set the database provider on every service. `UseKeycloak(...)` adds Keycloak with a realm named after the app, the users you list and a sign-in page in the app's colors. Any piece can be configured (`ConfigurePostgres`, `ConfigureRedis`, ...) or replaced with a resource of your own (`UseDatabase`, `UseRedis`, `UseSearch`).

## Grafana with Prometheus, Loki and Tempo

`options.UseGrafana()` adds Grafana, Prometheus, Loki, Tempo and an OpenTelemetry collector. The services keep sending to the Aspire dashboard and send the same metrics, logs and traces to the collector as well. Six dashboards are provisioned on every start:

- **Overview**: requests per second, errors, p95 latency, SignalR connections, busiest and slowest endpoints
- **.NET runtime**: CPU, memory, GC, thread pool, exceptions by type
- **Database access**: Entity Framework Core queries, SaveChanges, connection pool, slowest database spans
- **PostgreSQL server**: connections, cache hit ratio, deadlocks (Postgres only)
- **App data**: users, sign-ins, tenants, outbox and background jobs
- **Logs and traces**: every log line with filters, traces linked to their logs

Every dashboard filters by service and links to the others.

## WebDataStudio: the database in the browser

[![WebDataStudio](../assets/brands/webdatastudio.svg){ .cw-product-logo }](https://fgilde.github.io/WebDataStudio/)

`options.UseWebDataStudio()` adds a database studio with a ready connection to the app database: tables, SQL, charts and exports. With SQLite it opens the database file. Like Grafana it signs in with the administrator's email and password, and with the Keycloak button when Keycloak is on. Both tools are added in run mode only; `aspire publish` leaves them out.

![WebDataStudio showing a table of the app database](../assets/screenshots/webdatastudio-dark.webp){ .shot loading=lazy }

## AspireUI and the Services page

[![AspireUI](../assets/brands/aspireui.svg){ .cw-product-logo }](https://fgilde.github.io/AspireUI/)

AspireUI hosts more apps and stacks next to yours. With Keycloak, one call lets the app's administrators sign in there as admins:

```csharp
builder.AddAspireUI().WithCoworkeeSignOn(app).WithRetrieveApi();
```

Every service learns the addresses of the others, the Aspire dashboard, the Hangfire dashboard, Mailpit and every container with a web endpoint. The admin page *Services* shows them as tiles with their live state, probed every ten seconds, and with `WithRetrieveApi()` the apps of AspireUI appear there too.

![The Services admin page with tiles for each service, its address and whether it is running](../assets/screenshots/services-dark.webp){ .shot loading=lazy }

## LibreTranslate and Ollama containers

Two more containers are one option away. `UseLibreTranslate([...])` runs a LibreTranslate server for [machine translation](localization.md) with only the language models you need, kept in a volume shared by all apps on the machine. `coworkee new --ai-provider ollama` adds an Ollama container that pulls the model for the [AI assistant](../modules/ai.md), so the app runs offline without any key.

## Where to go next

- [Aspire app host](../hosting/aspire.md): roles, conventions, own infrastructure, Keycloak and publishing to Docker Compose or Azure
- [Grafana and WebDataStudio](../hosting/observability.md): every resource and dashboard, sign-in and switching them on and off
- [Scaling out with Redis](../hosting/redis.md): what Redis carries between instances
- [Service addresses](../hosting/aspire.md#service-addresses) and the [Services page](../modules/settings.md#services)
- [AspireUI](../hosting/aspire.md#aspireui): single sign-on through the app's Keycloak realm
