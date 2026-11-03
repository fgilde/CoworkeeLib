namespace Coworkee.Domain;

public interface IRealtimeTopicMapper
{
    IEnumerable<string> TopicsFor(object entity);
}
