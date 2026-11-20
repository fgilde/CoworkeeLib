namespace Coworkee.Contracts.Realtime;

/// <summary>
/// A change of an entity as a client hears it (<c>Realtime.On&lt;EntityChanged&lt;OrderDto&gt;&gt;</c>): the <see cref="EntityChangedPayload"/> of the topic
/// <c>type:{Entity}</c> with who changed it and when, from the envelope. The entity name comes from <typeparamref name="TDto"/>: its name without a trailing
/// "Dto" (<c>OrderDto</c> follows <c>type:Order</c>), or the topic given to <c>On</c>. Only ids and changed property names travel, never values.
/// </summary>
public sealed record EntityChanged<TDto>(string EntityType, string EntityId, string Action, IReadOnlyList<string> ChangedProperties, ActorInfo? Actor, DateTimeOffset? At)
{
    public bool IsDeleted => Action == EntityChangedPayload.Deleted;

    /// <summary>The entity name <typeparamref name="TDto"/> stands for.</summary>
    public static string Entity { get; } = typeof(TDto).Name.EndsWith("Dto", StringComparison.Ordinal) && typeof(TDto).Name.Length > 3
        ? typeof(TDto).Name[..^3]
        : typeof(TDto).Name;
}
