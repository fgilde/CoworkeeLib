# Workflows and approvals

`Coworkee.Workflows` gives entities a small state machine: states and transitions declared in code, guards on every transition, actions when a state is entered or left, approvals by roles or users with a deadline, a reminder and an escalation, and a full history of who did what, when and why. It is optional and opt-in per entity type. For processes that administrators build themselves in a designer, add [Elsa workflows](workflows-elsa.md) on top.

```csharp
[DependsOn(typeof(CoworkeeWorkflowsModule))]
public sealed class MyAppCatalogModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeWorkflows(workflows => workflows.Entity<Product>("Products", product =>
        {
            product.Initial("Draft").Describe(p => p.Name).Link(id => $"/catalog/products?id={id}").MirrorTo(p => p.Status);
            product.Transition("Submit", "Draft", "In review").RequiresPermission(CatalogPermissions.Products.Edit).When(p => p.Rate > 0);
            product.Transition("Withdraw", "In review", "Draft").RequiresPermission(CatalogPermissions.Products.Edit);
            product.Transition("Reopen", ["Approved", "Rejected"], "Draft").RequiresRole("Product Manager").RequiresComment();
            product.State("In review").Approval("Approved", "Rejected", approval => approval
                .Role("Manager").All()
                .DueIn(TimeSpan.FromDays(3))
                .RemindAfter(TimeSpan.FromDays(1))
                .EscalateTo("Administrator"));
            product.State("Approved").Set(p => p.ReleasedAt = DateTimeOffset.UtcNow).NotifyRole("Sales");
        }));
}
```

`AddCoworkeeWorkflows` may be called by several modules. The entity is loaded through the `DbContext`, so tenant filters apply; an id of another organisation is simply not found.

## States and transitions

- **`Initial`** names the state a new entity starts in. The state lives in the module's own table, so the entity class does not change. `MirrorTo(p => p.Status)` writes the state name into a property of the entity as well, for columns, filters and facets.
- **`Describe(...)`** names the entity in notifications and the inbox. The entity is loaded without navigations; `Include(l => l.Employee)` loads one with it, for the title, guards and actions: `leave.Include(l => l.Employee).Describe(l => $"Vacation of {l.Employee.Name}")`.
- **`Transition(name, from, to)`** declares a move a user can run; `from` may be several states.
- **Guards**: `RequiresPermission`, `RequiresRole` (one of the roles, directly or through a group), `When(entity => ...)` and `When(async context => ...)`. Every guard has to pass, on top of the permission `Workflows.Transition`. `RequiresComment()` makes the comment mandatory.
- **Actions**: `Then(context => ...)` on a transition, `OnEnter` and `OnExit` on a state, `Set(entity => ...)` to change fields, `NotifyRole(role)` to tell the members of a role. Actions get the tracked entity; what they change is saved together with the transition. `context.NotifyAsync(...)` and `context.UsersInRoleAsync(...)` cover the usual cases.

Only the transitions whose guards pass are offered to the user, and the server checks them again when one runs.

## Approvals

A state with `Approval(approved, rejected, ...)` asks for an approval when the workflow enters it:

- **Approvers**: `Role(...)` (the members at the moment the approval starts) `User(...)` and `UsersFrom(context => ...)`, which finds approvers from the entity when the approval starts (for example the manager of the requester, or a fallback role through `context.UsersInRoleAsync`). One decision is enough by default; `All()` waits for every approver. A rejection always decides. `NotByRequester()` keeps the person who ran the transition out of the approvers (four eyes), also when the escalation role joins.
- **Deadline**: `DueIn(...)` with `RemindAfter(...)` (one reminder to whoever has not decided yet) and `EscalateTo(role)`: at the deadline the members of that role join and one of them decides alone. `OnTimeout(state)` lets the approval expire and moves the workflow on instead.
- Once decided, the workflow moves to the approved or rejected state by itself. Leaving the state through a transition (e.g. *Withdraw*) cancels the open approval.

The reminders and deadlines are handled by the recurring job `workflow-deadlines` (every five minutes; `Coworkee:Workflows:DeadlineCron` changes it). Approvers get in-app notifications when an approval starts, when it is overdue and as a reminder.

## History, audit and realtime

Every transition, decision, reminder, escalation and expiry is a line of the history with the user, the time and the comment. State changes and approvals also land in the [audit log](auditing.md) like changes of any entity. Every change is sent on the realtime topic `workflow:{entityType}:{entityId}` (`WorkflowTopics.Entity`); the approvals of a user reach their own topic, so the inbox follows along.

## Extending it

Handlers of the domain events `WorkflowTransitioned`, `ApprovalStarted` and `ApprovalDecided` (`IDomainEventHandler<T>`) run after the commit through the outbox, e.g. to send a mail, call another system or start an [Elsa workflow](workflows-elsa.md). The commands are ordinary requests (`RunTransitionCommand`, `DecideApprovalCommand`), so the [AI assistant and MCP clients](ai.md) can use them too, with the user's permissions.

## Permissions

| Permission | |
|---|---|
| `Workflows.View` | see states, histories and the own approvals inbox; decide approvals the user is an approver of |
| `Workflows.Transition` | run transitions at all; each transition may ask for more |

On top of that, the workflow of a row is only there for users who see the row the way its [OData set](../web/odata.md) shows it: the set's permission, the organisation, soft delete and every `IODataEntityFilter<T>` row filter. Everyone else gets 404 on the state, the history, the comments, transitions and the realtime topic, as if the row did not exist. One exception: the approvers of the row's open approval see its state and history and decide, even without seeing the row in its list, because they have to see what they decide; they cannot run transitions through it, and once the approval is decided the exception ends. The approvals inbox always shows a user's own open approvals.

| Endpoint | |
|---|---|
| `GET /api/v1/workflows/{entityType}/{entityId}` | state, the transitions the user may run, the open approval and the history |
| `POST /api/v1/workflows/{entityType}/states` | states of up to 200 rows `{ "ids": [...] }` in one request, without history; rows the user cannot see are left out |
| `POST /api/v1/workflows/{entityType}/{entityId}/{transition}` | run a transition with `{ "comment" }` |
| `GET /api/v1/workflows/approvals` | the open approvals waiting for the user |
| `POST /api/v1/workflows/approvals/{id}` | decide with `{ "approve": true, "comment" }` |

## Database

The module adds the tables `cw.WorkflowItems`, `cw.WorkflowSteps`, `cw.WorkflowApprovals` and `cw.WorkflowApprovers`; create a migration in the app after adding it.

## In the client

```razor
<CoworkeeWorkflow EntityType="Products" EntityId="product.Id" OnChanged="_ => ReloadAsync()" />
```

`CoworkeeWorkflow` shows the state, a button for every transition the user may run, the open approval with its approvers and their decisions (*Approve* and *Reject* for the user's own), and the history as a timeline; `ShowHistory="false"` leaves the timeline out, `CoworkeeWorkflowTimeline` shows it alone. In a data table, `CoworkeeWorkflowColumn` shows the state as a chip and the same actions in a menu; the rows of a page ask for their states together (one request per entity type):

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products">
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <CoworkeeWorkflowColumn T="ProductDto" EntityType="Products" Title="@L["Approval"]" />
    </Columns>
</CoworkeeDataTable>
```

`CoworkeeWorkflowColumn` behaves like a property column: its filter lists the states of the workflow with their counts plus *Awaiting my approval*, it sorts, and it groups (the group keys come from the loaded states). After a transition it reloads the table, so filters and counts follow (`ReloadOnChange="false"` turns that off). The id of a row is its `Id` property; `EntityId="row => row.Key"` picks another one. `CoworkeeWorkflowActions` stays for a `TemplateColumn` of your own.

### Filter, sort and facet by state

Every OData set whose entity type has a workflow gets two virtual properties. They are no columns of the entity and never part of the response, but `$filter` and `$orderby` know them and the database evaluates them:

| Property | |
|---|---|
| `WorkflowState` | the state; a subquery on `cw.WorkflowItems` (rows without an item are in the initial state), or the mirrored column with `MirrorTo` |
| `WorkflowAwaitsMe` | `true` when an open approval waits for the decision of the current user |

```http
GET /odata/Products?$filter=WorkflowState eq 'In review'&$orderby=WorkflowState,Name
GET /odata/Products?$filter=WorkflowAwaitsMe eq true
```

The subquery runs through the query filters of the app's `DbContext`, so a state of another organisation never matches. With facets on, the response carries the facet `WorkflowState`: every state the workflow names (initial state, declared states, the ends of transitions and approvals) with its count, then *Awaiting my approval*. `WithColor` gives a state a CSS color for swatches:

```csharp
product.State("Approved").WithColor("var(--mud-palette-success)");
```

Both properties need `Workflows.View`; without it a query that names them is refused (403) and the facet is left out. An entity type with several workflows uses the one whose entity type is named like the set.

!!! note "Client entities"
    Rows mirrored into the browser (client entities) are queried locally, where `WorkflowState` does not exist. For such sets mirror the state into a string property (`MirrorTo(o => o.ApprovalState)`), give it a facet with `[ProvideFacet]` and name it in the column: `<CoworkeeWorkflowColumn ... MirrorProperty="ApprovalState" />`. The column then filters, sorts and groups on the property, in the browser and on the server alike. A mirrored workflow has no `WorkflowState` facet (the property's own facet replaces it); `$filter=WorkflowState ...` still works and reads the mirrored column.

A transition that needs a comment and every rejection ask for it in a small dialog. The page **Approvals** (`/workflows/approvals`) lists everything that waits for the user's decision, oldest first, with the deadline; it appears in the navigation for users with `Workflows.View`. State and transition names go through the localizer, so add them to the app's texts.

## In a new app

`coworkee new MyApp --workflows` (or the *Workflows and approvals* card in the Studio) adds the module. With the samples, products get the workflow above in short: product managers submit, an administrator approves within three days, and the products table has an *Approval* column to filter, sort and group by.
