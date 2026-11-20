using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Realtime;
using Coworkee.Domain;

namespace Coworkee.Application.Events;

/// <summary>
/// Something saved about one entity: who, when, why and what. The change collector scans every save once and hands these to its sinks (audit trail,
/// realtime, webhooks, workflows, <see cref="IEntityChangeHandler{TEntity}"/>, <see cref="IEntityEvents"/>). <see cref="Changes"/> follow the audit
/// rules: values as JSON, sensitive ones as <c>"***"</c>, none for types or properties that are not audited.
/// </summary>
public record EntityChange : IDomainEvent
{
    /// <summary>The CLR name of the entity's root type (the name of realtime topics, <c>type:{EntityType}</c>).</summary>
    public required string EntityType { get; init; }

    /// <summary>The key, its parts joined by commas.</summary>
    public required string EntityId { get; init; }

    public required ChangeAction Action { get; init; }

    public IReadOnlyList<PropertyChange> Changes { get; init; } = [];

    /// <summary>Who saved it; null for the system and for anonymous records.</summary>
    public ActorInfo? Actor { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>Why, from <see cref="ChangeReason"/> or a request with <see cref="IHasChangeReason"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>The trace of the request that saved it.</summary>
    public string? CorrelationId { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>An <see cref="EntityChange"/> of <typeparamref name="TEntity"/> (or a type derived from it): what handlers and subscribers of one type get.</summary>
public sealed record EntityChange<TEntity> : EntityChange
    where TEntity : class
{
    [JsonConstructor]
    public EntityChange()
    {
    }

    [SetsRequiredMembers]
    public EntityChange(EntityChange change)
        : base(change)
    {
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ChangeAction>))]
public enum ChangeAction
{
    Created,
    Updated,

    /// <summary>Removed, also by a soft delete.</summary>
    Deleted,

    /// <summary>A soft delete taken back.</summary>
    Restored,
}

/// <summary>One property: old and new value as JSON (<c>"***"</c> when sensitive), owned values as one JSON object.</summary>
public sealed record PropertyChange(string Property, string? OldValue, string? NewValue);
