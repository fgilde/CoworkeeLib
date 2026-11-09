using System.Reflection;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows;

/// <summary>The states and transitions of one entity type; built with <see cref="WorkflowOptions.Entity{TEntity}"/>.</summary>
public abstract class EntityWorkflow(string entityType)
{
    private readonly Dictionary<string, WorkflowState> _states = new(StringComparer.Ordinal);

    public string EntityType { get; } = entityType;

    public string? InitialState { get; protected set; }

    public IReadOnlyCollection<WorkflowState> States => _states.Values;

    public List<WorkflowTransition> Transitions { get; } = [];

    internal Func<Guid, string>? LinkOf { get; set; }

    internal Func<object, string>? TitleOf { get; set; }

    internal Action<object, string>? Mirror { get; set; }

    /// <summary>The property <see cref="EntityWorkflow{TEntity}.MirrorTo"/> writes the state into.</summary>
    internal PropertyInfo? MirrorProperty { get; set; }

    /// <summary>Every state the workflow names: the initial one, the declared ones, the ends of transitions and approvals.</summary>
    public IReadOnlyList<string> StateNames =>
    [
        .. new[] { InitialState! }
            .Concat(States.Select(s => s.Name))
            .Concat(Transitions.SelectMany(t => t.From.Append(t.To)))
            .Concat(States.Where(s => s.Step is not null).SelectMany(s => s.Step!.ApprovedTargets.Concat(s.Step.RejectedTargets).Append(s.Step.TimeoutState)))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Every role the workflow names: approvers, escalations, transitions and notifications.</summary>
    public IReadOnlyList<string> RoleNames =>
    [
        .. States.SelectMany(s => s.NotifiedRoles.Concat(s.Step?.Roles ?? []).Concat(s.Step?.EscalationRoles ?? []))
            .Concat(Transitions.SelectMany(t => t.Roles))
            .Distinct(StringComparer.Ordinal),
    ];

    internal abstract Type ClrType { get; }

    internal abstract Task<object?> FindAsync(CoworkeeDbContext db, Guid id, CancellationToken cancellationToken);

    /// <summary>The entity when the current unit of work added it and has not saved it yet, with the included references its foreign keys point to.</summary>
    internal abstract Task<object?> FindAddedAsync(CoworkeeDbContext db, Guid id, CancellationToken cancellationToken);

    /// <summary>Whether the current user sees the entity the way its OData set shows it: set permission, tenant, soft delete and row filters.</summary>
    internal abstract Task<bool> VisibleAsync(IServiceProvider services, Guid id, CancellationToken cancellationToken);

    internal abstract WorkflowContext Context(object entity, Guid id, WorkflowMove move, IServiceProvider services, CancellationToken cancellationToken);

    internal string Title(object entity, Guid id) => TitleOf?.Invoke(entity) ?? $"{EntityType} {id.ToString()[..8]}";

    internal WorkflowState? FindState(string name) => _states.GetValueOrDefault(name);

    protected TState GetOrAdd<TState>(string name, Func<TState> create)
        where TState : WorkflowState
    {
        if (!_states.TryGetValue(name, out var state))
        {
            _states[name] = state = create();
        }

        return (TState)state;
    }

    internal void Validate()
    {
        if (InitialState is null)
        {
            throw new InvalidOperationException($"The workflow of {EntityType} has no initial state; call Initial(...).");
        }

        foreach (var state in States.Where(s => s.Step is not null))
        {
            state.Step!.Validate(EntityType, state.Name);
        }
    }
}
