namespace Coworkee.Domain;

public interface IHasRealtimeTopics
{
    IEnumerable<string> RealtimeTopics { get; }
}
