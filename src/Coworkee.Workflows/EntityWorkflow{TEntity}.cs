using System.Linq.Expressions;
using System.Reflection;
using Coworkee.Application.Groups;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Workflows;

public sealed class EntityWorkflow<TEntity>(string entityType) : EntityWorkflow(entityType)
    where TEntity : class
{
    internal override Type ClrType => typeof(TEntity);

    public EntityWorkflow<TEntity> Initial(string state)
    {
        InitialState = state;
        return this;
    }

    /// <summary>Where notifications and the approvals inbox lead, e.g. <c>id =&gt; $"/catalog/products?id={id}"</c>.</summary>
    public EntityWorkflow<TEntity> Link(Func<Guid, string> link)
    {
        LinkOf = link;
        return this;
    }

    private Func<IQueryable<TEntity>, IQueryable<TEntity>> _load = query => query;

    private readonly List<string> _includes = [];

    /// <summary>Loads a navigation with the entity, for <see cref="Describe"/>, guards and actions, e.g. <c>l =&gt; l.Employee</c>.</summary>
    public EntityWorkflow<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigation)
    {
        var load = _load;
        _load = query => load(query).Include(navigation);
        if (navigation.Body is MemberExpression { Expression: ParameterExpression } member)
        {
            _includes.Add(member.Member.Name);
        }

        return this;
    }

    /// <summary>How the entity is called in notifications and the inbox, e.g. its name; navigations need <see cref="Include{TProperty}"/>.</summary>
    public EntityWorkflow<TEntity> Describe(Func<TEntity, string> title)
    {
        TitleOf = entity => title((TEntity)entity);
        return this;
    }

    /// <summary>Writes the state name into a string property of the entity as well, for filters and columns.</summary>
    public EntityWorkflow<TEntity> MirrorTo(Expression<Func<TEntity, string?>> property)
    {
        var info = (property.Body as MemberExpression)?.Member as PropertyInfo
            ?? throw new ArgumentException("Pass a property, e.g. p => p.Status.", nameof(property));
        Mirror = (entity, state) => info.SetValue(entity, state);
        MirrorProperty = info;
        return this;
    }

    public WorkflowTransition<TEntity> Transition(string name, string from, string to) => Transition(name, [from], to);

    public WorkflowTransition<TEntity> Transition(string name, IReadOnlyList<string> from, string to)
    {
        var transition = new WorkflowTransition<TEntity>(name, from, to);
        Transitions.Add(transition);
        return transition;
    }

    public WorkflowState<TEntity> State(string name) => GetOrAdd(name, () => new WorkflowState<TEntity>(name));

    internal override async Task<object?> FindAsync(CoworkeeDbContext db, Guid id, CancellationToken cancellationToken) =>
        await _load(db.Set<TEntity>()).FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);

    // ponytail: only direct references (x => x.Category) are loaded for added entities; deeper paths and collections stay as the command set them
    internal override async Task<object?> FindAddedAsync(CoworkeeDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var entry = db.ChangeTracker.Entries<TEntity>().FirstOrDefault(e => e.State == EntityState.Added && e.Property("Id").CurrentValue is Guid key && key == id);
        if (entry is null)
        {
            return null;
        }

        foreach (var reference in _includes.Select(entry.Navigation).OfType<ReferenceEntry>().Where(r => r.CurrentValue is null))
        {
            var keys = reference.Metadata is INavigation { IsOnDependent: true } navigation
                ? navigation.ForeignKey.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray()
                : null;
            if (keys is not null && keys.All(k => k is not null))
            {
                reference.CurrentValue = await db.FindAsync(reference.Metadata.TargetEntityType.ClrType, keys, cancellationToken);
            }
        }

        return entry.Entity;
    }

    internal override async Task<bool> VisibleAsync(IServiceProvider services, Guid id, CancellationToken cancellationToken)
    {
        using var unnarrowed = GroupScope.Unnarrowed();
        return await ODataEntityAccess.IsGrantedAsync(services, typeof(TEntity), cancellationToken)
            && await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken)).AnyAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);
    }

    internal override WorkflowContext Context(object entity, Guid id, WorkflowMove move, IServiceProvider services, CancellationToken cancellationToken) =>
        new WorkflowContext<TEntity>((TEntity)entity)
        {
            EntityType = EntityType,
            EntityId = id,
            From = move.From,
            To = move.To,
            Transition = move.Transition,
            Comment = move.Comment,
            UserId = move.UserId,
            Services = services,
            CancellationToken = cancellationToken,
            Link = LinkOf?.Invoke(id),
            Title = Title(entity, id),
        };
}
