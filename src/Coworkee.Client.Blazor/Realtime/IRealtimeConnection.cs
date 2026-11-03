using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Realtime;

public interface IRealtimeConnection : IAsyncDisposable
{
    event Func<Task>? Reconnected;

    Task StartAsync();

    Task InvokeAsync(string method, string topic);

    void OnEvent(Action<RealtimeEnvelope> handler);
}
