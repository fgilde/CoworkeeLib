# Elsa workflows

`Coworkee.Workflows.Elsa` hosts [Elsa Workflows 3](https://elsa-workflows.github.io/elsa-documentation/) (MIT, 3.9 with .NET 10 builds) inside the API of a Coworkee app. Use it when administrators should build long running processes themselves, beyond the states and approvals declared in code with [Workflows and approvals](workflows.md): "when a product is approved, wait two days, query the open orders and notify sales".

```csharp
[DependsOn(typeof(MyAppDatabaseModule), typeof(CoworkeeWorkflowsElsaModule))]
public sealed class MyAppApiModule : CoworkeeModule, IWebModule { ... }
```

Add it to the API host only: Elsa runs its own background services there. The module depends on `CoworkeeWorkflowsModule`.

## What it sets up

- **Database**: Elsa's workflow definitions and runtime tables live in the app's database (Postgres, SQL Server or SQLite, the same connection as the app) with Elsa's own migrations, which run when the API starts. `Coworkee:Workflows:Elsa:ConnectionString` puts them elsewhere.
- **API**: Elsa's REST API under `/api/elsa`, reached through the BFF like the rest of the API. Only signed-in users with the permission `Workflows.Elsa.Manage` get through; Elsa's own users and roles are not used. Elsa's endpoints are left out of the app's OpenAPI document and SDKs, and the app's validation errors keep their property names (`Brand.Name`).
- **Code-first workflows**: `services.AddWorkflow<MyWorkflow>()` registers a workflow written in C#.

## Activities and triggers

| Activity | Category *Coworkee* |
|---|---|
| **Workflow state reached** (trigger) | starts or resumes when an entity's workflow reaches a state, e.g. `Products` reaching `Approved`; outputs the entity id and the user |
| **Entity changed** (trigger) | starts when an entity of a watched type is created, updated or deleted |
| **Wait for approval** | waits for the decision on the entity's approval and continues with *Approved*, *Rejected* or *Expired* |
| **Run transition** | runs a transition of an entity's workflow as a user; the guards apply |
| **Send command** | sends any command or query of the app through the dispatcher as a user (`RunTransitionCommand`, an own `CreateOrderCommand`, ...), with the user's permissions, validation and unit of work; the result comes back as JSON |
| **Notify** | in-app notification to users and the members of a role |
| **Query OData** | reads an OData entity set of the app, e.g. `Products` with `$filter=Rate gt 100` |

*Run as* is the user an activity acts as; left empty, it is the user whose action started the workflow. Whoever may design workflows can therefore act as any user, which is why `Workflows.Elsa.Manage` belongs to administrators only.

Entity types whose changes start workflows are opt-in:

```csharp
services.AddCoworkeeElsa(elsa => elsa.Watch<Product>("Products"));
```

Triggers are fed after the commit through the outbox: workflow transitions and decisions (the domain events of the workflows module) and the changes of watched entities, which are written to the outbox in the same transaction. Nothing is lost when Elsa is busy, and a failing workflow does not roll back the user's change.

**Query OData** calls the app's own OData endpoint over HTTP with a personal API key of a service user, so that user's permissions apply:

| Setting | |
|---|---|
| `Coworkee:Workflows:Elsa:ApiUrl` | the API's base address, e.g. `https://api.example.com` |
| `Coworkee:Workflows:Elsa:ApiKey` | a personal API key (keep it in user secrets or a vault) |

## Elsa Studio

Elsa Studio, the visual designer, runs as an **app of its own** next to the Blazor app: `Coworkee.Workflows.Elsa.Studio` turns a small ASP.NET project into an Elsa Studio host (Blazor Server). It is not embedded in the Blazor app on purpose: Studio is a complete Blazor application with its own router, layout, MudBlazor providers, Radzen, a diagram library and Monaco, and every user of the app would download it, with or without the permission.

```csharp
// MyApp.ElsaStudio/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkeeElsaStudio();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapCoworkeeElsaStudio();
app.Run();
```

The project is `Microsoft.NET.Sdk.Web` with `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` (it has no `.razor` files of its own, the framework scripts have to be asked for) and the package `Coworkee.Workflows.Elsa.Studio`.

- **Sign-in**: the host signs in at the app's auth server like the web app, as an OIDC client of its own (`Coworkee:Bff`, the same settings as the web app, see [Clients and scopes](../security/authentication.md#clients-and-scopes)): same users, same sign-in page, Keycloak included. The tokens stay on the server; the browser only holds a session cookie.
- **Permission**: Studio calls the Elsa API of the api (`Coworkee:Workflows:Elsa:Studio:BackendUrl`, e.g. `https://api.example.com/api/elsa`) server side with the user's access token, so the API decides. Users without `Workflows.Elsa.Manage` see a page that says so instead of the designer.
- **Activities**: the palette shows the category *Coworkee* with the activities and triggers above; their inputs and outputs come from the API.
- **Link**: the app's navigation entry *Workflow designer* (System group, for `Workflows.Elsa.Manage`) goes to `/api/v1/workflows/elsa/studio`, which redirects to `Coworkee:Workflows:Elsa:StudioUrl`.

### In the Aspire app host

```csharp
app.AddElsaStudio<Projects.MyApp_ElsaStudio>();   // or AddProjects(), which finds *_ElsaStudio
```

`AddElsaStudio` registers the client `{app}-elsastudio` at the auth server (redirect `/signin-oidc`, API scope), gives the host its own session cookie, the address of the first api's Elsa API and sets `Coworkee:Workflows:Elsa:StudioUrl` of the apis to the designer's address. Add it after the api. In a deployment it is a tool like Grafana: `coworkee deploy --without elsastudio` leaves it out, its public address is the parameter `<app>-elsastudio-url` (see [Deployment](../hosting/deployment.md#tools)).

Without a running designer the link answers 404; workflows can still be written in C# (`WorkflowBase`) or sent as JSON to the API. Workflows written in C# show up in the designer and can be changed and published there; the published version stays after a restart.

## In a new app

`coworkee new MyApp --workflows --elsa` (or the *Elsa workflows* card in the Studio) adds the package to the API; `--elsa-studio` (card *Workflow designer*) adds the project `MyApp.ElsaStudio` and its line in the app host. With the samples, `MyApp.Api/Workflows/ProductApprovedWorkflow.cs` notifies the product managers when a product is approved.
