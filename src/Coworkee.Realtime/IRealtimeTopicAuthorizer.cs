namespace Coworkee.Realtime;

public interface IRealtimeTopicAuthorizer
{
    bool Handles(string topic);

    Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken);
}
