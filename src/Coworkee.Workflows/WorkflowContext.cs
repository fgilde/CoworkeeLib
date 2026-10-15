using Coworkee.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows;

/// <summary>What guards and actions see: the tracked entity (changes to it are saved with the transition), the move and who makes it.</summary>
public class WorkflowContext(object entity)
{
    public required string EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public object Entity { get; } = entity;

    public required string From { get; init; }

    public required string To { get; init; }

    /// <summary>The transition the user ran; null for moves of the workflow itself (approved, rejected, timed out).</summary>
    public string? Transition { get; init; }

    public string? Comment { get; init; }

    /// <summary>Null when the system moves (deadline job).</summary>
    public Guid? UserId { get; init; }

    public required IServiceProvider Services { get; init; }

    public CancellationToken CancellationToken { get; init; }

    internal string? Link { get; init; }

    internal string Title { get; init; } = string.Empty;

    /// <summary>Sends an in-app notification with a link to the entity; title and body are English texts with {0} for the entity's title.</summary>
    public Task NotifyAsync(IEnumerable<Guid> userIds, string title, string? body = null) =>
        Services.GetService<INotifier>() is { } notifier
            ? notifier.NotifyLocalizedAsync(userIds, "workflow", title, body, [Title, To], Link, CancellationToken)
            : Task.CompletedTask;

    /// <summary>The active users of the organisation with the role, directly or through a group.</summary>
    public Task<IReadOnlyList<Guid>> UsersInRoleAsync(string role) =>
        Services.GetRequiredService<ApproverDirectory>().UsersInRoleAsync([role], CancellationToken);
}

public sealed class WorkflowContext<TEntity>(TEntity entity) : WorkflowContext(entity)
    where TEntity : class
{
    public new TEntity Entity => (TEntity)base.Entity;
}
