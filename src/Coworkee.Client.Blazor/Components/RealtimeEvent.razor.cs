using Coworkee.Client.Blazor.Realtime;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The component form of <see cref="RealtimeClient.On{T}"/>: <c>&lt;RealtimeEvent T="EntityChanged&lt;OrderDto&gt;" OnEvent="ReloadAsync" SkipOwn="true" /&gt;</c>.
/// A changed <see cref="Topic"/> or <see cref="SkipOwn"/> subscribes again; leaving the page unsubscribes.
/// </summary>
public partial class RealtimeEvent<T> : IAsyncDisposable
{
    private IAsyncDisposable? _subscription;
    private (string? Topic, bool SkipOwn)? _subscribed;

    [Inject] private RealtimeClient Realtime { get; set; } = null!;

    [Parameter] public EventCallback<T> OnEvent { get; set; }

    /// <summary>Another topic than the default of <typeparamref name="T"/>, e.g. <c>entity:Order:{id}</c>.</summary>
    [Parameter] public string? Topic { get; set; }

    /// <summary>Leave out what the signed-in user caused himself.</summary>
    [Parameter] public bool SkipOwn { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_subscribed == (Topic, SkipOwn))
        {
            return;
        }

        _subscribed = (Topic, SkipOwn);
        await DisposeAsync();
        _subscription = await Realtime.On<T>(value => InvokeAsync(() => OnEvent.InvokeAsync(value)), Topic, SkipOwn);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }

        GC.SuppressFinalize(this);
    }
}
