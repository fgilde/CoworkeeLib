using System.Text.Json;
using Coworkee.Application.Messaging;
using Elsa.Workflows;
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
