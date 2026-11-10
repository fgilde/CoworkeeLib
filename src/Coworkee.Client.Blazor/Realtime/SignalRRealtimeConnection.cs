using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coworkee.Client.Blazor.Realtime;

/// <summary>
/// The hub connection. It opens a WebSocket straight away, without the negotiate request: behind a load balancer with several instances
/// (Container Apps scales out) the negotiate request and the WebSocket that follows it land on different instances, and the second one answers 404
/// for a connection id it never issued; the connection only stood after a few attempts. Where WebSockets do not get through (a proxy without
/// upgrade), the negotiated connection with its fallbacks takes over.
/// </summary>
internal sealed class SignalRRealtimeConnection : IRealtimeConnection
{
    private readonly Uri _url;
    private readonly List<Action<RealtimeEnvelope>> _handlers = [];
    private HubConnection _connection;
    private bool _negotiated;

    public SignalRRealtimeConnection(NavigationManager navigation)
    {
        _url = navigation.ToAbsoluteUri(RealtimeHubMethods.Path);
        _connection = Build(negotiate: false);
    }

    public event Func<Task>? Reconnected;

    public async Task StartAsync()
    {
        try
        {
            await _connection.StartAsync();
        }
        catch (Exception) when (!_negotiated)
        {
            await _connection.DisposeAsync();
            _negotiated = true;
            _connection = Build(negotiate: true);
            try
            {
                await _connection.StartAsync();
            }
            catch
            {
                // neither got through: the hub is down rather than the WebSocket blocked, the next attempt starts direct again
                await _connection.DisposeAsync();
                _negotiated = false;
                _connection = Build(negotiate: false);
                throw;
            }
        }
    }

    public Task InvokeAsync(string method, string topic) => _connection.InvokeAsync(method, topic);

    public void OnEvent(Action<RealtimeEnvelope> handler)
    {
        _handlers.Add(handler);
        _connection.On(RealtimeHubMethods.OnEvent, handler);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private HubConnection Build(bool negotiate)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(_url, options =>
            {
                if (!negotiate)
                {
                    options.SkipNegotiation = true;
                    options.Transports = HttpTransportType.WebSockets;
                }
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();
        connection.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
        foreach (var handler in _handlers)
        {
            connection.On(RealtimeHubMethods.OnEvent, handler);
        }

        return connection;
    }
}
