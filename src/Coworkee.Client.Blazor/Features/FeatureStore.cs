using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Features;

namespace Coworkee.Client.Blazor.Features;

/// <summary>The features of the current tenant, loaded once and reloaded when the server reports a change.</summary>
public sealed class FeatureStore(IFeaturesApi api, RealtimeClient realtime)
{
    private Task<IReadOnlyDictionary<string, string?>>? _values;
    private IAsyncDisposable? _subscription;

    public event Action? Changed;

    public async Task<string?> GetAsync(string feature) => (await (_values ??= LoadAsync())).GetValueOrDefault(feature);

    public async Task<bool> IsEnabledAsync(string feature) => bool.TryParse(await GetAsync(feature), out var enabled) && enabled;

    public void Reset()
    {
        _values = null;
        Changed?.Invoke();
    }

    private async Task<IReadOnlyDictionary<string, string?>> LoadAsync()
    {
        _subscription ??= await realtime.SubscribeAsync(FeatureTopics.Changed, _ =>
        {
            Reset();
            return Task.CompletedTask;
        });
        try
        {
            return await api.GetFeaturesAsync();
        }
        catch (ApiException)
        {
            return new Dictionary<string, string?>();
        }
    }
}
