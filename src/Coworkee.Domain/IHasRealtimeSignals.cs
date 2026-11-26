namespace Coworkee.Domain;

/// <summary>
/// Topics that get a payload-free signal when the entity changes: no entity name, id or property names, and no actor in the envelope. For changes that
/// clients must follow without learning what changed (e.g. hidden row filters). Unlike <see cref="IHasRealtimeTopics"/>.
/// </summary>
public interface IHasRealtimeSignals
{
    IEnumerable<string> RealtimeSignals { get; }
}
