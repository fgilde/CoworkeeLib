# Workflows und Genehmigungen

`Coworkee.Workflows` gibt Entitäten eine kleine Zustandsmaschine: Zustände und Übergänge im Code deklariert, Bedingungen an jedem Übergang, Aktionen beim Betreten oder Verlassen eines Zustands, Genehmigungen durch Rollen oder Benutzer mit Frist, Erinnerung und Eskalation sowie eine vollständige Historie, wer was wann und warum getan hat. Das Modul ist optional und wird pro Entitätstyp freigeschaltet. Für Abläufe, die Administratoren selbst in einem Designer bauen, kommt [Elsa-Workflows](workflows-elsa.md) obendrauf.

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

`AddCoworkeeWorkflows` darf von mehreren Modulen aufgerufen werden. Die Entität wird über den `DbContext` geladen, Mandantenfilter gelten also; die Id einer anderen Organisation wird schlicht nicht gefunden.

## Zustände und Übergänge

- **`Initial`** nennt den Zustand, in dem eine neue Entität beginnt. Der Zustand liegt in der eigenen Tabelle des Moduls, die Entitätsklasse ändert sich also nicht. `MirrorTo(p => p.Status)` schreibt den Namen des Zustands zusätzlich in eine Eigenschaft der Entität, für Spalten, Filter und Facetten.
- **`Transition(name, from, to)`** deklariert einen Schritt, den ein Benutzer ausführen kann; `from` dürfen mehrere Zustände sein.
- **Bedingungen**: `RequiresPermission`, `RequiresRole` (eine der Rollen, direkt oder über eine Gruppe), `When(entity => ...)` und `When(async context => ...)`. Jede Bedingung muss erfüllt sein, zusätzlich zur Berechtigung `Workflows.Transition`. `RequiresComment()` macht den Kommentar zur Pflicht.
- **Aktionen**: `Then(context => ...)` am Übergang, `OnEnter` und `OnExit` am Zustand, `Set(entity => ...)` zum Setzen von Feldern, `NotifyRole(role)` für eine Nachricht an die Mitglieder einer Rolle. Aktionen bekommen die verfolgte Entität; was sie ändern, wird zusammen mit dem Übergang gespeichert. `context.NotifyAsync(...)` und `context.UsersInRoleAsync(...)` decken die üblichen Fälle ab.

Angeboten werden nur Übergänge, deren Bedingungen erfüllt sind, und der Server prüft sie beim Ausführen noch einmal.

## Genehmigungen

Ein Zustand mit `Approval(approved, rejected, ...)` fordert beim Betreten eine Genehmigung an:

- **Genehmigende**: `Role(...)` (die Mitglieder zum Zeitpunkt des Starts) und `User(...)`. Standardmäßig reicht eine Entscheidung; `All()` wartet auf alle. Eine Ablehnung entscheidet immer. `NotByRequester()` hält die Person, die den Übergang ausgelöst hat, aus dem Kreis heraus (Vier-Augen-Prinzip).
- **Frist**: `DueIn(...)` mit `RemindAfter(...)` (eine Erinnerung an alle, die noch nicht entschieden haben) und `EscalateTo(role)`: Bei Fristablauf kommen die Mitglieder dieser Rolle dazu, und eine Person davon entscheidet allein. `OnTimeout(state)` lässt die Genehmigung stattdessen verfallen und schaltet den Workflow weiter.
- Nach der Entscheidung wechselt der Workflow von selbst in den genehmigten oder abgelehnten Zustand. Wer den Zustand über einen Übergang verlässt (z. B. *Withdraw*), bricht die offene Genehmigung ab.

Erinnerungen und Fristen erledigt der wiederkehrende Job `workflow-deadlines` (alle fünf Minuten; `Coworkee:Workflows:DeadlineCron` ändert das). Genehmigende bekommen In-App-Benachrichtigungen, wenn eine Genehmigung startet, wenn sie überfällig ist und als Erinnerung.

## Historie, Audit und Echtzeit

Jeder Übergang, jede Entscheidung, Erinnerung, Eskalation und jeder Verfall ist eine Zeile der Historie mit Benutzer, Zeit und Kommentar. Zustandswechsel und Genehmigungen landen außerdem wie jede andere Änderung im [Audit-Log](auditing.md). Jede Änderung geht über das Echtzeit-Topic `workflow:{entityType}:{entityId}` (`WorkflowTopics.Entity`); die Genehmigungen einer Person erreichen ihr eigenes Topic, der Posteingang zieht also mit.

## Erweitern

Handler der Domain-Events `WorkflowTransitioned`, `ApprovalStarted` und `ApprovalDecided` (`IDomainEventHandler<T>`) laufen nach dem Commit über die Outbox, etwa um eine Mail zu schicken, ein anderes System aufzurufen oder einen [Elsa-Workflow](workflows-elsa.md) zu starten. Die Befehle sind gewöhnliche Requests (`RunTransitionCommand`, `DecideApprovalCommand`), also können auch der [KI-Assistent und MCP-Clients](ai.md) sie nutzen, mit den Berechtigungen des Benutzers.

## Berechtigungen

| Berechtigung | |
|---|---|
| `Workflows.View` | Zustände, Historien und den eigenen Genehmigungs-Posteingang sehen; Genehmigungen entscheiden, bei denen man genehmigt |
| `Workflows.Transition` | überhaupt Übergänge ausführen; jeder Übergang kann mehr verlangen |

| Endpunkt | |
|---|---|
| `GET /api/v1/workflows/{entityType}/{entityId}` | Zustand, die erlaubten Übergänge, die offene Genehmigung und die Historie |
| `POST /api/v1/workflows/{entityType}/states` | Zustände von bis zu 200 Zeilen `{ "ids": [...] }` in einer Anfrage, ohne Historie; Zeilen, die der Benutzer nicht sehen darf, fehlen |
| `POST /api/v1/workflows/{entityType}/{entityId}/{transition}` | Übergang ausführen mit `{ "comment" }` |
| `GET /api/v1/workflows/approvals` | die offenen Genehmigungen, die auf den Benutzer warten |
| `POST /api/v1/workflows/approvals/{id}` | entscheiden mit `{ "approve": true, "comment" }` |

## Datenbank

Das Modul bringt die Tabellen `cw.WorkflowItems`, `cw.WorkflowSteps`, `cw.WorkflowApprovals` und `cw.WorkflowApprovers` mit; leg nach dem Hinzufügen eine Migration in der App an.

## Im Client

```razor
<CoworkeeWorkflow EntityType="Products" EntityId="product.Id" OnChanged="_ => ReloadAsync()" />
```

`CoworkeeWorkflow` zeigt den Zustand, einen Button für jeden erlaubten Übergang, die offene Genehmigung mit den Genehmigenden und ihren Entscheidungen (*Genehmigen* und *Ablehnen* für die eigene) und die Historie als Zeitleiste; `ShowHistory="false"` lässt die Zeitleiste weg, `CoworkeeWorkflowTimeline` zeigt sie allein. In einer Tabellenzeile zeigt `CoworkeeWorkflowActions` den Zustand als Chip und dieselben Aktionen in einem Menü. Die Zeilen einer Seite fragen ihre Zustände gemeinsam ab (eine Anfrage pro Entity-Typ):

```razor
<TemplateColumn T="ProductDto" Title="@L["Approval"]">
    <CellTemplate>
        <CoworkeeWorkflowActions EntityType="Products" EntityId="context.Item.Id" />
    </CellTemplate>
</TemplateColumn>
```

Ein Übergang mit Pflichtkommentar und jede Ablehnung fragen in einem kleinen Dialog danach. Die Seite **Genehmigungen** (`/workflows/approvals`) listet alles, was auf die Entscheidung des Benutzers wartet, älteste zuerst, mit Frist; sie erscheint in der Navigation für Benutzer mit `Workflows.View`. Namen von Zuständen und Übergängen laufen durch den Localizer, nimm sie also in die Texte der App auf.

## In einer neuen App

`coworkee new MyApp --workflows` (oder die Karte *Workflows and approvals* im Studio) fügt das Modul hinzu. Mit den Beispielen bekommen Produkte den Workflow von oben in kurz: Produktmanager reichen ein, ein Administrator genehmigt innerhalb von drei Tagen, und die Produkttabelle hat eine Spalte *Freigabe*.
