using System.Collections.Concurrent;
using System.Reflection;

namespace Coworkee.Contracts.Realtime;

/// <summary>
/// The stable realtime name of an event type, e.g. <c>[EventName("orders.approved")] public sealed record OrderApproved(Guid Id);</c>: the server publishes it with
/// <c>RealtimeEvents.PublishAsync(target, new OrderApproved(id))</c>, the client hears it with <c>Realtime.On&lt;OrderApproved&gt;(...)</c>. Renaming or moving the
/// type keeps the name. Without the attribute the name is the type's short name.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class EventNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public static class EventNames
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();

    /// <summary>The realtime name of <paramref name="type"/>: its <see cref="EventNameAttribute"/>, else its short name (never the full CLR name).</summary>
    public static string Of(Type type) => Names.GetOrAdd(type, t => t.GetCustomAttribute<EventNameAttribute>()?.Name ?? t.Name);

    public static string Of<T>() => Of(typeof(T));
}
