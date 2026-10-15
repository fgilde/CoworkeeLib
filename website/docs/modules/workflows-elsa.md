# Elsa workflows

`Coworkee.Workflows.Elsa` hosts [Elsa Workflows 3](https://elsa-workflows.github.io/elsa-documentation/) (MIT, 3.9 with .NET 10 builds) inside the API of a Coworkee app. Use it when administrators should build long running processes themselves, beyond the states and approvals declared in code with [Workflows and approvals](workflows.md): "when a product is approved, wait two days, query the open orders and notify sales".

```csharp
[DependsOn(typeof(MyAppDatabaseModule), typeof(CoworkeeWorkflowsElsaModule))]
public sealed class MyAppApiModule : CoworkeeModule, IWebModule { ... }
```

Add it to the API host only: Elsa runs its own background services there.

## What it sets up

- **Database**: Elsa's workflow definitions and runtime tables live in the app's database (Postgres, SQL Server or SQLite, the same connection as the app) with Elsa's own migrations, which run when the API starts. `Coworkee:Workflows:Elsa:ConnectionString` puts them elsewhere.
- **API**: Elsa's REST API under `/api/elsa`, reached through the BFF like the rest of the API. Only signed-in users with the permission `Workflows.Elsa.Manage` get through; Elsa's own users and roles are not used.
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

Elsa Studio, the visual designer, is **not embedded** in the Blazor app. It is a complete Blazor application of its own: its own router, layout and MudBlazor providers, Radzen, a diagram library in JavaScript and Monaco, and modules that initialize at start-up and expect to own the root component. Inside the Coworkee WebAssembly app it would compete with the app's MudBlazor providers and layout, and every user would download the designer's several megabytes, with or without the permission.

The library does not ship such a host yet. Run Studio as its own small Blazor WebAssembly app instead (Elsa's `Elsa.Studio` packages, see the Elsa documentation) under the web app's origin, so it talks to Elsa through the BFF with the user's session:

1. Host Studio at e.g. `/elsa-studio/` and point its backend at `/api/elsa` of the web app. Requests then carry the user's session cookie through the BFF.
2. Add an HTTP handler to Studio's backend client (`BackendApiConfig.ConfigureHttpClientBuilder`) that sends the header `X-CSRF: 1`, which the BFF expects on every changing request.
3. Serve Studio under the web app's origin: from the web host itself as a second WebAssembly app with the path base `/elsa-studio`, or behind one reverse proxy together with the web app.
4. Set `Coworkee:Workflows:Elsa:StudioUrl` to `/elsa-studio/`: the navigation entry *Workflow designer* (System group, for `Workflows.Elsa.Manage`) leads there.

Without Studio, workflows are written in C# (`WorkflowBase`) or as JSON through the API.

## In a new app

`coworkee new MyApp --workflows --elsa` (or the *Elsa workflows* card in the Studio) adds the package to the API. With the samples, `MyApp.Api/Workflows/ProductApprovedWorkflow.cs` notifies the product managers when a product is approved.
