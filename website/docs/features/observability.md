---
title: ".NET Aspire hosting with Grafana and dev tools"
description: "One AddCoworkeeApp call wires databases, Redis, Mailpit, Elasticsearch and Keycloak in the .NET Aspire app host, with Grafana dashboards and a database browser."
---

# Aspire and observability

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

That is a whole app host. `AddProjects()` finds the referenced projects by name (`*_Migrations`, `*_Auth`, `*_Api`, `*_Worker`, `*_Web`, `*_AirWorks`, `*_ElsaStudio`) and wires each in its role; a generated app calls `AddMigrations`, `AddAuthServer`, `AddApi` and `AddWeb` one by one instead. Either way the API gets its token authority, the web app its OIDC client at the auth server, and the migrations run before anything touches the database. Sharemee adds its processing worker, whose name fits no convention, with one more line: `app.AddWorker<Projects.Shareme_Processing_Host>("processing")`.

## Containers from the packages

Coworkee reads which packages each service uses and starts only the containers they need:

| Package in the service | It gets |
|---|---|
| `Coworkee.Infrastructure` | the app database; waits for the migrations |
| `Coworkee.Realtime` | Redis as SignalR backplane and for cache invalidation (`WithoutRedis()` leaves it out) |
| `Coworkee.Mailing` | Mailpit with a web inbox |
| `Coworkee.Storage` | the shared folder `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |

PostgreSQL is the default. `UseSqlServer()` swaps in SQL Server, `UseSqlite()` a file next to `src` and no container at all. `UseKeycloak(...)` adds Keycloak with a realm named after the app and a sign-in page in its colors. Anything can be tuned (`ConfigurePostgres`, `ConfigureRedis`) or replaced with your own resource (`UseDatabase`, `UseRedis`, `UseSearch`).

Optional containers: `UseLibreTranslate([...])` for [machine translation](localization.md); `coworkee new --ai-provider ollama` puts an Ollama container into the app host that pulls the model for the [assistant](../modules/ai.md), no API key needed, and `app.AddOpenWebUI(ollama)` adds [Open WebUI](../hosting/aspire.md#open-webui), a chat over its models; `UseKibana()` puts [Kibana](../hosting/aspire.md#kibana) next to Elasticsearch. Single-page apps of your own get a client at the auth server with `app.AddSpaClient(...)` ([single-page apps](../hosting/aspire.md#single-page-apps)).

## Grafana and WebDataStudio

![Grafana overview dashboard with requests per second, errors, latency and log volume per service](../assets/screenshots/grafana-overview-dark.webp){ .shot loading=lazy }

`UseGrafana()` brings Grafana with Prometheus, Loki, Tempo and an OpenTelemetry collector. The services keep reporting to the Aspire dashboard and send the same telemetry to the collector too. Six dashboards are provisioned on each start: Overview, .NET runtime, Database access (Entity Framework Core queries, connection pool, slowest spans), PostgreSQL server, App data (users, sign-ins, outbox, background jobs) and Logs and traces.

[![WebDataStudio](../assets/brands/webdatastudio.svg){ .cw-product-logo }](https://fgilde.github.io/WebDataStudio/)

`UseWebDataStudio()` opens the app database in the browser, with tables, SQL, charts and exports. On SQLite it mounts the database file.

Both sign in with the app's administrator, or at the app's auth server as an administrator of the system organisation, as do AspireUI, Kibana and Open WebUI.

## AspireUI and the Services page

[![AspireUI](../assets/brands/aspireui.svg){ .cw-product-logo }](https://fgilde.github.io/AspireUI/)

AspireUI hosts further apps and stacks next to yours. One call lets the app's administrator in, makes the administrators of the system organisation AspireUI admins through the app's auth server and gives it a public address in a deployment:

```csharp
builder.AddAspireUI().WithCoworkee(app).WithRetrieveApi();
```

Every service knows the addresses of the others, plus the Aspire and Hangfire dashboards, Mailpit and any container with a web endpoint. The admin page *Services* shows them as tiles and probes them every ten seconds; with `WithRetrieveApi()` the AspireUI apps join the list.

![The Services admin page with tiles for each service, its address and whether it is running](../assets/screenshots/services-dark.webp){ .shot loading=lazy }

## Deployment

`coworkee deploy compose|azure|k8s` publishes the app host for Docker Compose, Azure Container Apps or Kubernetes, with the tools of the app host (Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, AspireUI, Kibana, Open WebUI) and public addresses for each; `--without grafana` leaves one out. See [Deployment](../hosting/deployment.md).

**Status:** Docker Compose is verified with a generated app; the Azure Container Apps and Kubernetes outputs build and validate but have not run a real deployment. Running more than one instance needs Redis and a shared Data Protection key ring.

Reference: [Aspire app host](../hosting/aspire.md), [Deployment](../hosting/deployment.md), [Grafana and WebDataStudio](../hosting/observability.md), [scaling out with Redis](../hosting/redis.md), [AspireUI](../hosting/aspire.md#aspireui).
