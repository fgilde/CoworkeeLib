using Coworkee.Client.Blazor.Api;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// Optimistic toggles that save one after another in the background: a change shows at once, the next click need not wait for the last save, and a failed
/// save takes back only its own item. Serialized, so the server sees the clicks in the order they were made.
/// </summary>
internal sealed class PendingToggles<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, (bool On, int Version)> _pending = [];
    private Task _queue = Task.CompletedTask;
    private int _version;

    /// <summary>What the item shows: its latest unsaved toggle, else <paramref name="saved"/>.</summary>
    public bool Shows(TKey key, bool saved) => _pending.TryGetValue(key, out var pending) ? pending.On : saved;

    /// <summary>The items toggled on that are not saved yet, e.g. rows to show before the server lists them.</summary>
    public IEnumerable<TKey> PendingOn => _pending.Where(p => p.Value.On).Select(p => p.Key);

    public bool IsSaving(TKey key) => _pending.ContainsKey(key);

    /// <summary>Shows the toggle at once and queues its save.</summary>
    /// <param name="save">Runs after every earlier save has finished.</param>
    /// <param name="settled">Runs once the save ended: with <c>null</c> after success (keep the new state), with the reason after a failure (the item shows its saved state again).</param>
    /// <returns>The queue up to this save; callers need not wait for it.</returns>
    public Task ToggleAsync(TKey key, bool on, Func<Task> save, Func<string?, Task> settled)
    {
        var version = ++_version;
        _pending[key] = (on, version);
        return _queue = RunAsync(_queue, key, version, save, settled);
    }

    private async Task RunAsync(Task previous, TKey key, int version, Func<Task> save, Func<string?, Task> settled)
    {
        await previous;
        string? failure = null;
        try
        {
            await save();
        }
        catch (ApiException exception)
        {
            failure = SnackbarApiExtensions.Describe(exception);
        }
        catch (HttpRequestException exception)
        {
            failure = exception.Message;
        }

        // a later click on the same item stays pending; settled updates the saved state before the next render
        if (_pending.TryGetValue(key, out var pending) && pending.Version == version)
        {
            _pending.Remove(key);
        }

        await settled(failure);
    }
}
