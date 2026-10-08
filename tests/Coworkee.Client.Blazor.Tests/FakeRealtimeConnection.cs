using System.Text.Json;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Tests;

public sealed class FakeRealtimeConnection : IRealtimeConnection
{
    private Action<RealtimeEnvelope>? _handler;

    public List<(string Method, string Topic)> Invocations { get; } = [];

    public event Func<Task>? Reconnected;

    public int FailStarts { get; set; }

    public Task StartAsync() => FailStarts-- > 0 ? throw new HttpRequestException("offline") : Task.CompletedTask;

    public Task InvokeAsync(string method, string topic)
    {
        Invocations.Add((method, topic));
        return Task.CompletedTask;
    }

    public void OnEvent(Action<RealtimeEnvelope> handler) => _handler = handler;

    public Task ReconnectAsync() => Reconnected?.Invoke() ?? Task.CompletedTask;

    public void Push(string topic) => _handler?.Invoke(new RealtimeEnvelope(topic, RealtimeEventTypes.EntityChanged, JsonSerializer.SerializeToElement(new { })));

    public void Push(string topic, string type, object payload) => _handler?.Invoke(new RealtimeEnvelope(topic, type, JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web)));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
