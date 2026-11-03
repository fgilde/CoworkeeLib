namespace Coworkee.Workflows;

/// <summary>A transition users run; every guard has to pass: the permission, one of the roles and every condition.</summary>
public sealed class WorkflowTransition<TEntity>(string name, IReadOnlyList<string> from, string to) : WorkflowTransition(name, from, to)
    where TEntity : class
{
    public WorkflowTransition<TEntity> RequiresPermission(string permission)
    {
        Permission = permission;
        return this;
    }

    /// <summary>The user has one of the roles (by name), directly or through a group.</summary>
    public WorkflowTransition<TEntity> RequiresRole(params string[] roles)
    {
        Roles.AddRange(roles);
        return this;
    }

    public WorkflowTransition<TEntity> When(Func<TEntity, bool> condition)
    {
        Guards.Add(context => Task.FromResult(condition((TEntity)context.Entity)));
        return this;
    }

    public WorkflowTransition<TEntity> When(Func<WorkflowContext<TEntity>, Task<bool>> condition)
    {
        Guards.Add(context => condition((WorkflowContext<TEntity>)context));
        return this;
    }

    public WorkflowTransition<TEntity> RequiresComment()
    {
        CommentRequired = true;
        return this;
    }

    /// <summary>Runs after the state changed, before the enter actions of the new state.</summary>
    public WorkflowTransition<TEntity> Then(Func<WorkflowContext<TEntity>, Task> action)
    {
        Actions.Add(context => action((WorkflowContext<TEntity>)context));
        return this;
    }
}
