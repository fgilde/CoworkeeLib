using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Workflows;
using Coworkee.Notifications;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>Sends any Coworkee command or query through the dispatcher as a user: the user's permissions, validation and the unit of work apply.</summary>
[Activity("Coworkee", "Coworkee", "Sends a command or query of the app as a user, e.g. RunTransitionCommand with {\"entityType\":\"Products\",...}.")]
public sealed class SendCommand : CodeActivity
{
    [Input(Description = "The request type, by name (RunTransitionCommand) or full name.")]
    public Input<string> RequestType { get; set; } = default!;

    [Input(Description = "The request as JSON.", UIHint = "multi-line")]
    public Input<string> Payload { get; set; } = new("{}");

    [Input(Description = "The user to act as; empty: the user who started the workflow.")]
    public Input<Guid?> RunAs { get; set; } = default!;

    [Output(Description = "The result as JSON.")]
    public Output<string> Result { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var type = RequestTypes.Find(context.Get(RequestType)!);
        var request = JsonSerializer.Deserialize(context.Get(Payload) ?? "{}", type, JsonSerializerOptions.Web)!;
        await using var actor = await ElsaActor.BeginAsync(context, context.Get(RunAs));
        var result = await RequestTypes.SendAsync(actor.Services.GetRequiredService<IDispatcher>(), request, context.CancellationToken);
        context.Set(Result, JsonSerializer.Serialize(result, JsonSerializerOptions.Web));
    }
}

/// <summary>Runs a transition of an entity's Coworkee workflow as a user; the transition's guards apply.</summary>
[Activity("Coworkee", "Coworkee", "Runs a transition of an entity's workflow as a user, e.g. Submit.")]
public sealed class RunTransition : CodeActivity
{
    [Input(Description = "The entity type, e.g. Products.")]
    public Input<string> EntityType { get; set; } = default!;

    [Input(Description = "The id of the entity.")]
    public Input<Guid> EntityId { get; set; } = default!;

    [Input(Description = "The transition, e.g. Submit.")]
    public Input<string> Transition { get; set; } = default!;

    [Input(Description = "An optional comment for the history.")]
    public Input<string?> Comment { get; set; } = default!;

    [Input(Description = "The user to act as; empty: the user who started the workflow.")]
    public Input<Guid?> RunAs { get; set; } = default!;

    [Output(Description = "The state after the transition.")]
    public Output<string> State { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        await using var actor = await ElsaActor.BeginAsync(context, context.Get(RunAs));
        var result = await actor.Services.GetRequiredService<IDispatcher>().SendAsync(
            new RunTransitionCommand(context.Get(EntityType)!, context.Get(EntityId), context.Get(Transition)!, context.Get(Comment)), context.CancellationToken);
        context.Set(State, result.IsSuccess ? result.Value.State : throw new InvalidOperationException(result.Error.Message));
    }
}

/// <summary>Waits until the open (or next) approval of an entity is decided and continues with the outcome.</summary>
[Activity("Coworkee", "Coworkee", "Waits for the decision on an entity's approval.")]
[FlowNode(nameof(ApprovalStatus.Approved), nameof(ApprovalStatus.Rejected), nameof(ApprovalStatus.Expired))]
public sealed class WaitForApproval : Activity
{
    [Input(Description = "The entity type, e.g. Products.")]
    public Input<string> EntityType { get; set; } = default!;

    [Input(Description = "The id of the entity.")]
    public Input<Guid> EntityId { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context) =>
        context.CreateBookmark(new ApprovalStimulus(context.Get(EntityType)!, context.Get(EntityId)), ResumeAsync, includeActivityInstanceId: false);

    private static async ValueTask ResumeAsync(ActivityExecutionContext context) =>
        await context.CompleteActivityWithOutcomesAsync(ElsaValues.Text(context, ElsaInputs.Status) ?? nameof(ApprovalStatus.Expired));
}

/// <summary>Sends an in-app notification to users and the members of a role.</summary>
[Activity("Coworkee", "Coworkee", "Sends an in-app notification to users or the members of a role.")]
public sealed class Notify : CodeActivity
{
    [Input(Description = "User ids.")]
    public Input<ICollection<Guid>?> Users { get; set; } = default!;

    [Input(Description = "A role whose members are notified too.")]
    public Input<string?> Role { get; set; } = default!;

    [Input(Description = "The title.")]
    public Input<string> Title { get; set; } = default!;

    [Input(Description = "The text.", UIHint = "multi-line")]
    public Input<string?> Body { get; set; } = default!;

    [Input(Description = "Where the notification leads, e.g. /catalog/products.")]
    public Input<string?> Link { get; set; } = default!;

    [Input(Description = "The organisation is the one of this user; empty: the user who started the workflow.")]
    public Input<Guid?> RunAs { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        await using var actor = await ElsaActor.BeginAsync(context, context.Get(RunAs));
        var role = context.Get(Role);
        var users = (context.Get(Users) ?? []).Concat(role is { Length: > 0 }
            ? await actor.Services.GetRequiredService<ApproverDirectory>().UsersInRoleAsync([role], context.CancellationToken)
            : []);
        await actor.Services.GetRequiredService<INotifier>().NotifyAsync(users, "workflow", context.Get(Title)!, context.Get(Body), context.Get(Link), context.CancellationToken);
        await actor.SaveAsync(context.CancellationToken);
    }
}

/// <summary>
/// Reads an OData entity set of the app over HTTP with the API key of <c>Coworkee:Workflows:Elsa:ApiKey</c> (a personal key of a service user),
/// so the key owner's permissions apply.
/// </summary>
[Activity("Coworkee", "Coworkee", "Queries an OData entity set of the app, e.g. Products with $filter=Price gt 100.")]
public sealed class QueryOData : CodeActivity
{
    public const string HttpClientName = "coworkee-elsa-odata";

    [Input(Description = "The entity set, e.g. Products.")]
    public Input<string> EntitySet { get; set; } = default!;

    [Input(Description = "The query options, e.g. $filter=Price gt 100&$top=10.")]
    public Input<string?> Query { get; set; } = default!;

    [Output(Description = "The rows (the value array of the answer).")]
    public Output<JsonElement> Rows { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var http = context.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        var url = $"odata/{Uri.EscapeDataString(context.Get(EntitySet)!)}" + (context.Get(Query) is { Length: > 0 } query ? "?" + query.TrimStart('?') : string.Empty);
        var answer = await http.GetFromJsonAsync<JsonElement>(url, context.CancellationToken);
        context.Set(Rows, answer.TryGetProperty("value", out var rows) ? rows : answer);
    }
}
