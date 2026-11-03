using Coworkee.Notifications;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa.Activities;

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
