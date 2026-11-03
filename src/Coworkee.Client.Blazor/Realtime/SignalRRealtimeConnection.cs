using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coworkee.Client.Blazor.Realtime;

internal sealed class SignalRRealtimeConnection : IRealtimeConnection
{
    private readonly HubConnection _connection;

    public SignalRRealtimeConnection(NavigationManager navigation)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(RealtimeHubMethods.Path))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();
        _connection.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
    }

    public event Func<Task>? Reconnected;

    public Task StartAsync() => _connection.StartAsync();

    public Task InvokeAsync(string method, string topic) => _connection.InvokeAsync(method, topic);

    public void OnEvent(Action<RealtimeEnvelope> handler) => _connection.On(RealtimeHubMethods.OnEvent, handler);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
