# Aspire app host

`Coworkee.Aspire` turns the app host into a few lines. You name the app; Coworkee finds its services, adds the infrastructure each one needs and the settings that connect them.

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.LogoUrl = "/coworkee-icon.svg";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("info@coworkee.de", "Administrator", "MyApp")));
}).AddProjects();

builder.Build().Run();
```

`LogoUrl` is the logo of the sign-in pages while the theme has none: a path is taken on the web app (`{web}/coworkee-icon.svg`), an absolute address as it is.

Sharemee adds a worker whose name follows no convention after the others:

```csharp
var app = builder.AddCoworkeeApp("shareme", ...).AddProjects();
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
```

## Projects by convention

`AddProjects()` looks at the `Projects.*` types Aspire generates for the projects the app host references and adds them by name, in this order:

| Type name | Added with | Resource |
|---|---|---|
| `*_Migrations` | `AddMigrations` | `{name}-migrations` |
| `*_Auth` | `AddAuthServer` | `{name}-auth` |
| `*_Api` | `AddApi` | `{name}-api` |
| `*_Worker*`, `*Worker` | `AddWorker` | `{name}-worker` |
| `*_Web` | `AddWeb` | `{name}-web` |

The suffix is the part after the first underscore, lower case with dashes: `MyApp_Jobs_Worker` becomes `myapp-jobs-worker`. Other projects are left alone.

```csharp
builder.AddCoworkeeApp("myapp", options =>
{
    options.Configure("api", api => api.WithReplicas(2)); // by suffix, also for explicitly added services
    options.Skip("web", "MyApp_Admin_Api");                // by suffix or type name
}).AddProjects();
```

Explicit calls still work and win: a project added before `AddProjects()` is not added again. To give a discovered project another role or suffix, skip it and add it yourself afterwards.

## Roles

| Method | Resource | Wiring |
|---|---|---|
| `AddMigrations<T>()` | `{name}-migrations` | database, waits for the Postgres server, dashboard commands |
| `AddAuthServer<T>()` | `{name}-auth` | external endpoints, no job server, public auth URL, development certificates in Development, Keycloak provider |
| `AddApi<T>()` | `{name}-api` | token authority and audience `{name}_api`, API scope at the auth server, setup token from `Coworkee:SetupToken` |
| `AddWorker<T>(suffix)` | `{name}-{suffix}` | infrastructure only, no public endpoints |
| `AddWeb<T>()` | `{name}-web` | BFF settings, OIDC client at the auth server with redirect URIs, reference to the API, links to Swagger and the jobs dashboard |

Call them in this order (`AddProjects()` does). Calling `AddWeb` before `AddAuthServer` throws and says so.

## Infrastructure from the packages

Before wiring a service, Coworkee reads the service's `obj/project.assets.json` and looks at the packages and projects it uses, directly or through other projects:

| Package in the service | It gets |
|---|---|
| `Coworkee.Infrastructure` | connection string of the app database, waits until the migrations finished |
| `Coworkee.Realtime` | Redis for the SignalR backplane, the session changes between instances and the shared session stamp cache |
| `Coworkee.Mailing` | Mailpit as default SMTP server |
| `Coworkee.Storage` | the shared blob folder `.data/blobs` (local runs only; published containers keep their default) |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |
| `Coworkee.Notifications` | links in digest mails point to the web app |

Containers are only created when a service needs them. Postgres, Redis, Elasticsearch and Keycloak keep their data in volumes; `--Coworkee:EphemeralInfrastructure=true` starts them empty, which the tests use.

### Modules of your own

`app.Modules` holds this wiring by package or project name. App modules declare what they need the same way; every service that references the project, directly or transitively, gets it:

```csharp
var app = builder.AddCoworkeeApp("myapp");
var stripe = builder.AddConnectionString("stripe");
app.Modules.Add("MyApp.Billing", (app, service) => service.WithReference(stripe));
app.AddProjects();
```

Register modules before adding the services. `app.Modules[CoworkeeModules.Search] = ...` replaces a built-in wiring, `app.Modules.Remove(...)` drops it.

## Your own infrastructure

Every piece can be configured, replaced or switched off:

```csharp
builder.AddCoworkeeApp("myapp", options =>
{
    options.ConfigurePostgres(postgres => postgres.WithPgAdmin())
        .ConfigureRedis(redis => redis.WithRedisInsight())
        .ConfigureMail(mail => mail.WithLifetime(ContainerLifetime.Persistent))
        .ConfigureSearch(search => search.WithEnvironment("ES_JAVA_OPTS", "-Xmx1g"))
        .ConfigureKeycloak(keycloak => keycloak.WithLifetime(ContainerLifetime.Persistent));

    // services with Coworkee.Search.Elasticsearch get no Elasticsearch
    options.Without(CoworkeeModules.Search);
}).AddProjects();
```

| Option | Instead of |
|---|---|
| `UseDatabase(resource)` | the Postgres container; any resource with a connection string, e.g. `builder.AddConnectionString("myapp")` |
| `UseRedis(resource)` | the Redis container |
| `UseSearch(resource)` | the Elasticsearch container |
| `UseMail(endpoint)` | Mailpit; the SMTP endpoint of another resource |
| `UseKeycloak(resource, ...)` | the Keycloak container; the realm is imported into yours |

The services get supplied resources under the names they expect (`ConnectionStrings:myapp`, `redis`, `elasticsearch`). Coworkee does not wait for them; add `WaitFor` through `options.Configure` if you need it.

```csharp
// production: the connection string comes from configuration (ConnectionStrings:myapp) or is asked for on deploy
var database = builder.AddConnectionString("myapp");
builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database)).AddProjects();
```

## Dashboard

When the app host runs locally in Development, the migrations resource has two commands:

- **Re-run migrations** starts the migrations project again: new migrations and the demo data seed.
- **Reset database** drops the app database after a confirmation and runs the migrations again. It is only offered for the built-in Postgres container, never for a supplied database and never outside Development.

The web resource links to **Swagger** (`/swagger`) and, when an API uses `Coworkee.BackgroundJobs`, to the Hangfire dashboard (**Jobs**, `/admin/jobs`). Mailpit shows its inbox as **Mailpit UI**.

## Typed settings

Settings are set through the configuration tree, not through strings:

```csharp
api.WithSetting(s => s.Coworkee.Storage.Provider, "S3")
   .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], "smtp.example.com")
   .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/admin/jobs", "/hubs");
```

`s => s.Coworkee.Bff.Scopes[0]` becomes the environment variable `Coworkee__Bff__Scopes__0`. Values can be strings, numbers, booleans, endpoint references, reference expressions or parameters. `CoworkeeSettings` mirrors the real options classes from `Coworkee.Contracts.Configuration`, so a renamed property breaks the build of the app host, not the running app.

## Keycloak

`UseKeycloak` adds a Keycloak container and imports a realm named after the app with:

- a confidential client `{name}-auth` whose secret is a generated parameter (`{name}-keycloak-client-secret`),
- the users you list, all with one generated password (`{name}-keycloak-user-password`, shown in the dashboard and kept in the user secrets of the app host).

The auth server gets the provider settings and offers **Sign in with Keycloak**. `LoginMode` switches between both, external only and internal only; `Port` fixes the host port if you want a stable URL.

## Publishing

`PublishToDockerCompose()` adds a Docker Compose environment. `aspire publish` then writes a `docker-compose.yaml` and an `.env` file with all services, containers, volumes and the start order (services start once the migrations completed):

```csharp
builder.AddCoworkeeApp("myapp", options => options.PublishToDockerCompose(compose => compose.WithDashboard(false)))
    .AddProjects();
```

```bash
aspire publish -o deploy
```

Azure Container Apps works through the replaceable infrastructure; add `Aspire.Hosting.Azure.AppContainers` and `Aspire.Hosting.Azure.PostgreSQL` to the app host:

```csharp
builder.AddAzureContainerAppEnvironment("aca");
var database = builder.AddAzurePostgresFlexibleServer("postgres").RunAsContainer().AddDatabase("myapp");
builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database)).AddProjects();
```

## Production

The app host describes development. For production also configure real certificates (`Coworkee:Auth:SigningCertificate`, `EncryptionCertificate`), the public URLs behind your reverse proxy, a real SMTP server, storage and Keycloak. The dashboard of a local run lists the settings each service needs.
