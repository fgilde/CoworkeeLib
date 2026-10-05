using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class RealtimeSubscription : IAsyncDisposable
{
    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    private IAsyncDisposable? _subscription;

    [Parameter, EditorRequired] public string Topic { get; set; } = string.Empty;

    [Parameter] public EventCallback<RealtimeEnvelope> OnEvent { get; set; }

    protected override async Task OnInitializedAsync() =>
        _subscription = await Realtime.SubscribeAsync(Topic, envelope => InvokeAsync(() => OnEvent.InvokeAsync(envelope)));

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }
}
