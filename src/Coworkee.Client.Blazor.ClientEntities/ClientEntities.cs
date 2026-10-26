using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Data;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// The local copies of the OData sets the server marked as client entities. Data tables use them on their own;
/// call <see cref="SetAsync{T}"/> to query one in your own UI.
/// </summary>
public sealed class ClientEntities : IClientDataCache, IAsyncDisposable
{
    private readonly ClientEntityApi _api;
    private readonly IODataClient _server;
    private readonly IClientEntityStorage _storage;
    private readonly RealtimeClient? _realtime;
    private readonly ClientEntitySignal _signal;
    private readonly ILogger<ClientEntities> _logger;
    private readonly ClientEntityModes? _modes;
    private readonly Dictionary<string, ClientEntitySet> _sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task<IAsyncDisposable>> _subscriptions = [];
    private Task<ClientEntityManifest?>? _manifest;

    internal ClientEntities(ClientEntityApi api, IODataClient server, IClientEntityStorage storage, RealtimeClient? realtime, ClientEntitySignal signal, ILogger<ClientEntities> logger,
        ClientEntityModes? modes = null)
    {
        _modes = modes;
        _api = api;
        _server = server;
        _storage = storage;
        _realtime = realtime;
        _signal = signal;
        _logger = logger;
        _signal.Mutated += NudgeAll;
    }

    /// <summary>A set's status or rows changed.</summary>
    public event Action<ClientEntitySet>? Changed;

    public IReadOnlyCollection<ClientEntitySet> Sets => _sets.Values;

    public ClientEntitySet? Find(string entitySet) => _sets.GetValueOrDefault(entitySet);

    /// <summary>
    /// The local copy of <paramref name="entitySet"/> with rows of <typeparamref name="T"/>, started on first use. Null when the server does
    /// not offer the set to this user, <typeparamref name="T"/> has no Guid Id or the set is already mirrored with another row type.
    /// </summary>
    public async Task<ClientEntitySet<T>?> SetAsync<T>(string entitySet, string? expand = null, CancellationToken cancellationToken = default)
    {
        if (_sets.TryGetValue(entitySet, out var existing))
        {
            return existing as ClientEntitySet<T>;
        }

        if (!ClientEntitySet<T>.CanMirror || await ManifestAsync() is not { } manifest
            || manifest.Sets.FirstOrDefault(s => string.Equals(s.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase)) is not { } info)
        {
            return null;
        }

        if (_sets.TryGetValue(entitySet, out existing))
        {
            return existing as ClientEntitySet<T>;
        }

        var set = new ClientEntitySet<T>(info, expand, manifest.Partition, _server, _api, _storage, _logger);
        _sets[entitySet] = set;
        set.Changed += s => Changed?.Invoke(s);
        set.Start();
        _ = ApplyModeAsync(set);
        if (info.RealtimeTopic is { } topic && _realtime is not null)
        {
            // connecting to the hub must not hold back the first query
            _subscriptions.Add(_realtime.SubscribeAsync(topic, _ =>
            {
                set.Nudge();
                return Task.CompletedTask;
            }));
        }

        return set;
    }

    /// <summary>Runs the queries of <paramref name="entitySet"/> in the browser or on the server and remembers the choice for the user.</summary>
    public async Task SetModeAsync(string entitySet, ClientEntityMode mode)
    {
        if (Find(entitySet) is { } set)
        {
            set.Mode = mode;
        }

        if (_modes is not null)
        {
            await _modes.SetAsync(entitySet, mode);
        }
    }

    /// <summary>Drops every local copy, here and in the browser storage.</summary>
    public async Task ClearAsync()
    {
        await DisposeSetsAsync();
        _manifest = null;
        _modes?.Clear();
        await _storage.ClearAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _signal.Mutated -= NudgeAll;
        await DisposeSetsAsync();
    }

    private void NudgeAll()
    {
        foreach (var set in _sets.Values)
        {
            set.Nudge();
        }
    }

    // the copy downloads meanwhile, so the stored choice is there before the first local query
    private async Task ApplyModeAsync(ClientEntitySet set)
    {
        if (_modes is not null)
        {
            set.Mode = await _modes.GetAsync(set.EntitySet);
        }
    }

    // a failed manifest (signed out, server without client entities) is kept: tables then simply ask the server
    private Task<ClientEntityManifest?> ManifestAsync() => _manifest ??= LoadManifestAsync();

    private async Task<ClientEntityManifest?> LoadManifestAsync()
    {
        try
        {
            return await _api.ManifestAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "No client entities: the manifest did not load");
            return null;
        }
    }

    private async Task DisposeSetsAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            await (await subscription).DisposeAsync();
        }

        foreach (var set in _sets.Values)
        {
            await set.DisposeAsync();
        }

        _subscriptions.Clear();
        _sets.Clear();
    }
}
