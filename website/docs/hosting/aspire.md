# Aspire app host

`Coworkee.Aspire` turns the app host into a few lines. You name the app; Coworkee finds its services, adds the infrastructure each one needs and the settings that connect them.

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.LogoUrl = "/coworkee-icon.svg";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("info@coworkee.de", "Administrator", "MyApp") { Password = "Dev-Passw0rd!" }));
}).AddProjects();

builder.Build().Run();
```

`LogoUrl` is the logo of the sign-in pages while the theme has none: a path is taken on the web app (`{web}/coworkee-icon.svg`), an absolute address as it is.

`options.SignIn` sets layout, media and media side of the [sign-in pages](../security/sign-in-pages.md) (`Coworkee:Auth:Design` of the auth server); a media path is resolved on the web app like `LogoUrl`:

```csharp
options.SignIn.Layout = SignInLayouts.Split;
options.SignIn.MediaUrl = "/media/welcome.mp4";
options.SignIn.MediaSide = SignInMediaSide.Right;
```

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
| `*_AirWorks` | `AddAirWorks` | `{name}-airworks` |
| `*_ElsaStudio` | `AddElsaStudio` (in a deployment only while the tool `elsastudio` is on) | `{name}-elsastudio` |

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
| `AddMigrations<T>()` | `{name}-migrations` | database, waits for the database server, the modules it references like any service (blob storage for seeded files, ...), dashboard commands |
| `AddAuthServer<T>()` | `{name}-auth` | external endpoints, no job server, public auth URL, development certificates in Development, Keycloak provider |
| `AddApi<T>()` | `{name}-api` | token authority and audience `{name}_api`, API scope at the auth server, setup token (local run: the same for every service, shown on the console and in the dashboard while the system is not set up, see [Setup wizard](../security/authentication.md#setup-wizard-or-seed)) |
| `AddWorker<T>(suffix)` | `{name}-{suffix}` | infrastructure only, no public endpoints |
| `AddWeb<T>()` | `{name}-web` | BFF settings, OIDC client at the auth server with redirect URIs, reference to the API, links to Swagger and the jobs dashboard |
| `AddAirWorks<T>()` | `{name}-airworks` | like `AddWeb`: BFF settings, the next OIDC client at the auth server, its own session cookie name; for the [AirWorks client](../web/airworks.md) |
| `AddElsaStudio<T>()` | `{name}-elsastudio` | the next OIDC client at the auth server, its own session cookie, the Elsa API of the first api, `Coworkee:Workflows:Elsa:StudioUrl` of the apis; for the [workflow designer](../modules/workflows-elsa.md#elsa-studio) |

Call them in this order (`AddProjects()` does). Calling `AddWeb` before `AddAuthServer` throws and says so.

### Single-page apps

A browser app that signs in at the auth server itself and sends its access token to the API, without a BFF (a React or Angular app, for example), is a public client. `AddSpaClient` registers it for any resource with an HTTP endpoint, after the api:

```csharp
var app = builder.AddCoworkeeApp("shop").AddProjects();
var spa = builder.AddJavaScriptApp("spa", "../../frontend", "dev").WithHttpEndpoint(port: 4200, env: "PORT");
app.AddSpaClient("spa", spa, scopes: ["files"], apiScopeAliases: ["Shop"], allowImpersonation: true);
```

| What | Value |
|---|---|
| Client | `spa`, public (no secret), authorization code with PKCE plus refresh tokens, implicit consent |
| Redirect and post-logout address | the address of the endpoint (`endpointName`, default `http`) exactly, plus `redirectPath` when given; home address in the launcher |
| Scopes | the api's scope `{name}_api`, the `scopes` you pass and the aliases |
| API scope aliases | each alias is a further scope with the api's audience (`Coworkee:Auth:ApiScopes:Shop = shop_api`), for apps that ask for a scope of their own name |
| Impersonation | `allowImpersonation: true` lets [Log in as](../features/identity.md#log-in-as-another-user) reach the app |
| CORS | the origin goes into `Coworkee:Cors:Origins` of the auth server and of every api, also of apis added later (see [CORS](../security/clients.md#cross-origin-calls-cors)) |

In a deployment the address comes from the parameter `{name}-{resource}-url` (e.g. `https://spa.example.com`), like the public addresses of the services. The app itself needs the addresses of the auth server and the api; pass them as you need (`WithEnvironment`).

## Infrastructure from the packages

Before wiring a service, Coworkee reads the service's `obj/project.assets.json` and looks at the packages and projects it uses, directly or through other projects:

| Package in the service | It gets |
|---|---|
| `Coworkee.Infrastructure` | connection string of the app database and `Coworkee:Database:Provider`, waits until the migrations finished |
| `Coworkee.Realtime` | Redis for the SignalR backplane, presence, cache invalidation and session changes between instances ([Scaling out with Redis](redis.md)); `options.WithoutRedis()` leaves it out |
| `Coworkee.Mailing` | Mailpit as default SMTP server |
| `Coworkee.Storage` | the shared blob folder `.data/blobs` (local runs only; published containers keep their default) |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |
| `Coworkee.Notifications` | links in digest mails point to the web app |

Grafana (with Prometheus, Loki, Tempo and a collector) and WebDataStudio come with `options.UseGrafana()` and `options.UseWebDataStudio()`, see [Grafana and WebDataStudio](observability.md). Kibana comes with `options.UseKibana()`, see [Kibana](#kibana).

Containers are only created when a service needs them. Postgres, SQL Server, Redis, Elasticsearch and Keycloak keep their data in volumes; `--Coworkee:EphemeralInfrastructure=true` starts them empty, which the tests use.

### Kibana

`options.UseKibana()` adds Kibana next to the Elasticsearch container (not with `UseSearch`, an Elasticsearch of your own brings its own Kibana). Kibana's single sign-on (OpenID Connect, SAML) needs a paid Elastic license, so the app host puts [oauth2-proxy](https://oauth2-proxy.github.io/oauth2-proxy/) in front of it:

- The resource `kibana` is the proxy and the address you open. It signs in at the app's auth server (client `<app>-kibana`), and with Keycloak on, the auth server sends you on to Keycloak as for the app.
- The auth server lets only administrators of the system organisation through (`AuthClientOptions.SystemAdministratorsOnly`). Everybody else gets `access_denied`.
- `kibana-server` is Kibana itself. It lets every request in as the Elasticsearch user `elastic` (its anonymous provider, part of the free license), so nothing but the proxy should reach it: the dashboard shows no link to it and a deployment publishes no port of it (a local run keeps its port on the loopback address). `kibana-setup` gives the built-in user `kibana_system` its password once Elasticsearch answers.
- Kibana takes the image version of the Elasticsearch container. Its saved objects live in Elasticsearch, so they share its volume.

Without an auth server Kibana keeps its own login: user `elastic` with the password of Elasticsearch (parameter `elasticsearch-password`). `coworkee new --elasticsearch --kibana` (template option `kibana`, off by default) adds the call.

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
| `UseDatabase(resource)` | the database container; any resource with a connection string, e.g. `builder.AddConnectionString("myapp")` |
| `UseSqlServer(configure)` | Postgres: a SQL Server container (`app.SqlServer`), or with `UseDatabase` a SQL Server of your own |
| `UseSqlite(path)` | Postgres: a SQLite file, `.data/<name>.db` next to `src` unless given; no container |
| `UseRedis(resource)` | the Redis container |
| `UseSearch(resource)` | the Elasticsearch container |
| `UseMail(endpoint)` | Mailpit; the SMTP endpoint of another resource |
| `UseKeycloak(resource, ...)` | the Keycloak container; your Keycloak gets the realm |

`UseSqlServer` and `UseSqlite` also set `Coworkee:Database:Provider` on the services; the migrations of the app must be made for that provider (see [Database providers](../fundamentals/persistence.md#database-providers)).

The services get supplied resources under the names they expect (`ConnectionStrings:myapp`, `redis`, `elasticsearch`). Coworkee does not wait for them; add `WaitFor` through `options.Configure` if you need it.

```csharp
// production: the connection string comes from configuration (ConnectionStrings:myapp) or is asked for on deploy
var database = builder.AddConnectionString("myapp");
builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database)).AddProjects();
```

## Dashboard

When the app host runs locally in Development, the migrations resource has two commands:

- **Re-run migrations** starts the migrations project again: new migrations and the demo data seed.
- **Reset database** drops the app database after a confirmation and runs the migrations again. It is only offered for the built-in Postgres container, never for SQL Server, SQLite or a supplied database and never outside Development.

The web resource links to **Swagger** (`/swagger`) and, when an API uses `Coworkee.BackgroundJobs`, to the Hangfire dashboard (**Jobs**, `/admin/jobs`). Mailpit shows its inbox as **Mailpit UI**.

## Service addresses

Every service of the app gets the addresses of the others under `Coworkee:Services:<resource>`: the Aspire dashboard (`dashboard`), the Hangfire dashboard (`jobs`), each project (`myapp-api`, `myapp-auth`, `myapp-web`, with `HealthPath` `/health`) and every container with an http endpoint (`mail`, `pgadmin`, `keycloak`, ...), also resources added after `AddCoworkeeApp`. Inject them as `IOptionsMonitor<CoworkeeServicesOptions>`; the admin page **Services** shows them with their live state (see [Settings](../modules/settings.md#services)). A deployment lists the public addresses of its tools there; further ones go into appsettings.

An AspireUI in the app host adds the apps and stacks you host *yourself* with it to the same page. `builder.AddAspireUI().WithRetrieveApi()` is all it takes: the app host passes the address (and the key given to `WithRetrieveApi(...)`: a parameter stays a reference, never a resolved string; a literal key is passed as text) to every service as `Coworkee:AspireUI:Url` and `ApiKey`. The first AspireUI with the retrieve API is used; one without it is ignored, and a published app gets nothing (set `Coworkee:AspireUI` in appsettings). Services find them through `IServiceDirectory` (see [Settings](../modules/settings.md#service-directory)).

## Typed settings

Settings are set through the configuration tree, not through strings:

```csharp
api.WithSetting(s => s.Coworkee.Storage.Provider, "S3")
   .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], "smtp.example.com")
   .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/admin/jobs", "/hubs");
```

`s => s.Coworkee.Bff.Scopes[0]` becomes the environment variable `Coworkee__Bff__Scopes__0`. Values can be strings, numbers, booleans, endpoint references, reference expressions or parameters. `CoworkeeSettings` mirrors the real options classes from `Coworkee.Contracts.Configuration`, so a renamed property breaks the build of the app host, not the running app.

## Keycloak

`UseKeycloak` adds a Keycloak container with a realm named after the app:

- a confidential client `{name}-auth` whose secret is a generated parameter (`{name}-keycloak-client-secret`),
- the users you list, each with its `Password`; users without one share a generated password (`{name}-keycloak-user-password`, shown in the dashboard and kept in the user secrets of the app host).

The realm is brought up to date on **every start**, once Keycloak is healthy and before the auth server starts: a missing realm is created, an existing one (kept in the data volume) gets the current client secrets, redirect addresses, users and passwords through the admin API. A changed secret parameter or password therefore takes effect with the next start, and users or clients you added in the Keycloak console stay. The resource log of Keycloak says `Realm myapp is up to date: clients ..., users ...`.

The auth server gets the provider settings and offers **Sign in with Keycloak**. `LoginMode` switches between both, external only and internal only and is the default of the setting *Sign-in methods* (`Auth.LoginMode`), which administrators change at runtime (see [Sign-in rules](../security/registration.md#sign-in-rules)); `Port` fixes the host port if you want a stable URL.

The api hosts serve the settings, so they get `Coworkee:Auth:External:Mode` and the display name of the provider too, so that the setting is defined there; the client secret and the other provider settings stay with the auth server.

Browsers send the cookies of `localhost` to every port, so the cookies of all local apps reach Keycloak too. Over HTTPS Keycloak speaks HTTP/2, which by default takes only 8 KB of request headers and answers **431 Request Header Fields Too Large** beyond that. The container therefore gets `QUARKUS_HTTP_LIMITS_MAX_HEADER_LIST_SIZE=65536`, the 64 KB it takes over HTTP/1.1. If the browser still shows 431, delete the `localhost` cookies of other apps; the auth server and the web app (Kestrel) take 32 KB.

### Sign-in page in the app's design

Keycloak's own sign-in page looks like the app: the colors of the default theme (light and dark), its logo (or `LogoUrl`), the app's `DisplayName` above the form and the layout and media of `SignIn` (card, split or slide). Coworkee writes a login theme `coworkee-{name}` for it, a child of Keycloak's `keycloak.v2` theme with a stylesheet, a small script and the title message, no templates of its own, so it keeps working with newer Keycloak versions.

- The app host mounts `src/MyApp.AppHost/.coworkee/keycloak-themes` as `/opt/keycloak/themes` into the Keycloak container (the template's `.gitignore` leaves `.coworkee/` out) and writes the theme there on the first start (a folder that cannot be written is logged as warning and does not stop the realm), with the Ocean colors and the options of `AddCoworkeeApp`; paths like `/logo.svg` or `/media/welcome.mp4` become addresses of the web app.
- The realm reconcile sets the realm's `loginTheme`, `displayName` and `displayNameHtml` on every start, for a new realm and for one kept in the data volume.
- The api hosts get `Coworkee:Keycloak:ThemeDirectory`, `ThemeName`, `AppName`, `LogoUrl` and `BaseUrl` (the web app) and rewrite the theme at start, a second after a theme change is committed (saving a theme, making another one the default) or an administrator saves the *Sign-in page* design, and on *Apply to Keycloak* (see [Theming](../modules/theming.md#keycloak-sign-in-page)).
- `start-dev` does not cache themes, so a change shows with the next page load.

Turn it off with `options.UseKeycloak(k => k.LoginTheme = false)`: no mount, no theme, the realm keeps the theme it has (Keycloak falls back to its own when the folder is gone). A Keycloak of your own (`UseKeycloak(resource, ...)`) gets the realm's display name but no theme; mount the folder yourself if you want it.

### Other clients and the users

Other resources of the app host can sign in through the same realm and reuse its users, for example a dashboard with OpenID Connect:

```csharp
var app = builder.AddCoworkeeApp("myapp", options => options.UseKeycloak(k => k.Users.AddRange(users))).AddProjects();
var grafana = builder.AddContainer("grafana", "grafana/grafana").WithHttpEndpoint(targetPort: 3000);
var client = app.AddKeycloakClient("myapp-grafana", [ReferenceExpression.Create($"{grafana.GetEndpoint("http")}/login/generic_oauth")]);
grafana.WithEnvironment("GF_AUTH_GENERIC_OAUTH_CLIENT_ID", client.ClientId)
    .WithEnvironment("GF_AUTH_GENERIC_OAUTH_CLIENT_SECRET", client.Secret)
    .WithEnvironment("GF_AUTH_GENERIC_OAUTH_AUTH_URL", ReferenceExpression.Create($"{app.KeycloakAuthority}/protocol/openid-connect/auth"));
```

| Member of `CoworkeeApp` | |
|---|---|
| `AddKeycloakClient(clientId, redirectUris?, secret?)` | another confidential client of the realm; without a secret a generated one (`{clientId}-secret`), without redirect addresses any local address |
| `KeycloakClients` | the clients of the realm, the auth server's first |
| `KeycloakAuthority` | issuer address of the realm, `{keycloak}/realms/{realm}` |
| `KeycloakUsers`, `KeycloakPassword(user)` | the users and each one's password as reference expression, e.g. for the local login of another resource |

Call them after `UseKeycloak` and `AddAuthServer` (or `AddProjects`).

### AspireUI

[![AspireUI](../assets/brands/aspireui.svg){ .cw-product-logo }](https://fgilde.github.io/AspireUI/)

[AspireUI](https://fgilde.github.io/AspireUI/) signs in at the app's auth server with one call; the administrators of the system organisation become AspireUI admins:

```csharp
using Nextended.Aspire.Hosting.AspireUI;

var app = builder.AddCoworkeeApp("myapp").AddProjects();
builder.AddAspireUI().WithCoworkeeSignOn(app);
```

It registers the client `{app}-{resource}` at the auth server with the redirect address `/api/auth/sso/callback` and sets the `role` claim as groups. With Keycloak on, the auth server sends you on to Keycloak as for the app. AspireUI seeds settings only while they are empty, so the call also forces the app host's settings (`ASPIREUI_SET_FORCE`): a data volume from an earlier client would keep the old sign-in otherwise.

### Tools sign in at the auth server

Grafana, WebDataStudio, AspireUI, Open WebUI and Kibana's proxy sign in at the app's auth server, with or without Keycloak. Each gets a confidential client `{app}-{tool}` that is not in the launcher and lets only the administrators of the system organisation in (`SystemAdministratorsOnly`); they are admins of the tool. The local administrator login of each tool stays. While the app host runs, the auth server names itself by the browser's address (`Coworkee:Auth:Issuer`) and, with `Coworkee:Auth:DynamicBackchannel`, hands containers token, user info and keys at the address they asked by, so the containers reach it over the container network and the browser over the host. A deployment uses the public address for both.

### Open WebUI

[Open WebUI](https://openwebui.com/) is a chat in the browser over the models of an Ollama container:

```csharp
var ollama = builder.AddOllama("ollama");
app.AddOpenWebUI(ollama);
```

The app's administrator signs in locally (`WEBUI_ADMIN_EMAIL`, nobody signs up with the form), and with the auth server the administrators of the system organisation sign in through it as admins of Open WebUI (client `{app}-openwebui`, PKCE). The app host owns its settings (`ENABLE_PERSISTENT_CONFIG=false`): Open WebUI would otherwise keep the addresses of its first start in its database. Data in the volume `{app}-openwebui-data`. `coworkee new --ai-provider ollama --open-web-ui` (template option `openWebUI`) adds the call; `coworkee deploy --without openwebui` leaves it out of a deployment.

### AspireUI in a generated app

`coworkee new --aspire-ui` (template option `aspireUI`, off by default) adds the same lines to `src/MyApp.AppHost/AppHost.cs`, the package reference `Nextended.Aspire.Hosting.AspireUI` to the app host and a section to `LOGIN.md`: the administrator signs in locally with the app's email and password or at the auth server of the app (`WithCoworkee(app)`). The Studio shows it next to Grafana and WebDataStudio; the preset *Everything* switches it on.

```csharp
builder.AddAspireUI()
    .WithAdminUser(administrator.Email, administrator.Password)
    .WithSeedFromDirectory(builder.AppHostDirectory)
    .WithCoworkeeSignOn(app)
    .WithRetrieveApi();
```

## Publishing

`coworkee deploy compose|azure|k8s` publishes the app host for Docker Compose, Azure Container Apps or Kubernetes: public addresses, token certificates, the first administrator and the mail server as parameters, no demo users, and the tools of the app host (Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, Open WebUI, AspireUI) along unless switched off with `DeployTool` or `coworkee deploy --without`. `PublishToDockerCompose(configure)` still configures the Compose environment (`compose => compose.WithDashboard(false)`); an environment the app host adds itself before `AddCoworkeeApp` replaces the one of `Coworkee:Deploy:Target`. Everything else, and what is verified, is in [Deployment](deployment.md).
