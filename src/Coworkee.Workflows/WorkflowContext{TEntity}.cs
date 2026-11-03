namespace Coworkee.Workflows;

public sealed class WorkflowContext<TEntity>(TEntity entity) : WorkflowContext(entity)
    where TEntity : class
{
    public new TEntity Entity => (TEntity)base.Entity;
}
