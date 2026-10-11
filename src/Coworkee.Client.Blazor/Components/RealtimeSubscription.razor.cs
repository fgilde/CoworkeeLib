using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class RealtimeSubscription : IAsyncDisposable
{
    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    private IAsyncDisposable? _subscription;
    private string? _topic;

    /// <summary>Follows a changed topic, e.g. "entity:Role:{Id}" of a detail page that navigates to another role.</summary>
    [Parameter, EditorRequired] public string Topic { get; set; } = string.Empty;

    [Parameter] public EventCallback<RealtimeEnvelope> OnEvent { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Topic == _topic)
        {
            return;
        }

        _topic = Topic;
        await DisposeAsync();
        _subscription = Topic.Length == 0 ? null : await Realtime.SubscribeAsync(Topic, envelope => InvokeAsync(() => OnEvent.InvokeAsync(envelope)));
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }
    }
}
