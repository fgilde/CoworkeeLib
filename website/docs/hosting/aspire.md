# Aspire app host

`Coworkee.Aspire` turns the app host into a few lines. You name the services and their roles; Coworkee adds the infrastructure each one needs and the settings that connect them.

```csharp title="MyApp.AppHost/AppHost.cs"
var builder = DistributedApplication.CreateBuilder(args);

var app = builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "MyApp";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("info@coworkee.de", "Administrator", "MyApp")));
});

app.AddMigrations<Projects.MyApp_Migrations>();
app.AddAuthServer<Projects.MyApp_Auth>();
app.AddApi<Projects.MyApp_Api>();
app.AddWeb<Projects.MyApp_Web>();

builder.Build().Run();
```

Sharemee adds a worker for its processing queue:

```csharp
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
```

## Roles

| Method | Resource | Wiring |
|---|---|---|
| `AddMigrations<T>()` | `{name}-migrations` | database, waits for the Postgres server |
| `AddAuthServer<T>()` | `{name}-auth` | external endpoints, no job server, public auth URL, development certificates in Development, Keycloak provider |
| `AddApi<T>()` | `{name}-api` | token authority and audience `{name}_api`, API scope at the auth server, setup token from `Coworkee:SetupToken` |
| `AddWorker<T>(suffix)` | `{name}-{suffix}` | infrastructure only, no public endpoints |
| `AddWeb<T>()` | `{name}-web` | BFF settings, OIDC client at the auth server with redirect URIs, reference to the API, public app URL for notification mails |

Call them in this order. Calling `AddWeb` before `AddAuthServer` throws and says so.

## Infrastructure from the packages

Before wiring a service, Coworkee reads the service's `obj/project.assets.json` and looks at the Coworkee packages it uses, directly or through other projects:

| Package in the service | It gets |
|---|---|
| `Coworkee.Infrastructure` | connection string of the app database, waits until the migrations finished |
| `Coworkee.Realtime` | Redis for the SignalR backplane |
| `Coworkee.Mailing` | Mailpit as default SMTP server |
| `Coworkee.Storage` | the shared blob folder `.data/blobs` |
| `Coworkee.Search.Elasticsearch` | Elasticsearch |
| `Coworkee.Notifications` | links in digest mails point to the web app |

Containers are only created when a service needs them. Postgres, Redis, Elasticsearch and Keycloak keep their data in volumes; `--Coworkee:EphemeralInfrastructure=true` starts them empty, which the tests use.

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

## Production

The app host describes development. For production, take the settings from the dashboard of a local run as the list of what each service needs, and configure real certificates (`Coworkee:Auth:SigningCertificate`, `EncryptionCertificate`), a real SMTP server, storage and Keycloak.
