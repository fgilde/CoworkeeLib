using System.Text;
using System.Text.Json;
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
    // ponytail: the copies of the last three group selections stay for switching back; make it an option if users juggle more
    private const int KeptPartitions = 3;
    private const string OwnerKey = "#owner";
    private const string PartitionsKey = "#partitions";

    private readonly ClientEntityApi _api;
    private readonly IODataClient _server;
    private readonly IClientEntityStorage _storage;
    private readonly RealtimeClient? _realtime;
    private readonly ClientEntitySignal _signal;
    private readonly ILogger<ClientEntities> _logger;
    private readonly ClientEntityModes? _modes;
    private readonly IClientEntityEngine? _engine;
    private readonly Dictionary<string, ClientEntitySet> _sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task<IAsyncDisposable>> _subscriptions = [];
    private Task<ClientEntityManifest?>? _manifest;

    internal ClientEntities(ClientEntityApi api, IODataClient server, IClientEntityStorage storage, RealtimeClient? realtime, ClientEntitySignal signal, ILogger<ClientEntities> logger,
        ClientEntityModes? modes = null, IClientEntityEngine? engine = null)
    {
        _modes = modes;
        _engine = engine;
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

    public bool AnswersLocally(string entitySet) => Find(entitySet) is { Mode: ClientEntityMode.Client, Status.IsLocal: true };

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

        // an expand named by the server is the same in every session, whichever page asks first
        var set = new ClientEntitySet<T>(info, info.Expand ?? expand, manifest.Partition, _server, _api, _storage, _logger, _engine);
        _sets[entitySet] = set;
        set.Changed += s => Changed?.Invoke(s);
        set.Rescoped += () => _ = ScopeChangedAsync();
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
        _modes?.Clear();
        await _storage.ClearAsync();
    }

    /// <summary>
    /// The rows the user sees changed as a whole (another group selection): the copies here go and the sets start over with the manifest's new
    /// partition. The browser storage keeps the copies of the last selections, so switching back only brings their changes.
    /// </summary>
    public Task ScopeChangedAsync() => DisposeSetsAsync();

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
        ClientEntityManifest manifest;
        try
        {
            manifest = await _api.ManifestAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "No client entities: the manifest did not load");
            return null;
        }

        await KeepAsync(manifest);
        return manifest;
    }

    // before any set reads the storage: the copies of another user or tenant leave the device (also those a sign-out did not drop, e.g. after an
    // expired session); unmarked copies (from an older version) count as another user's. Of this user's group selections the last few stay.
    private async Task KeepAsync(ClientEntityManifest manifest)
    {
        try
        {
            if (manifest.Owner is { } owner && await _storage.LoadAsync(OwnerKey) is var stored && (stored is null || Encoding.UTF8.GetString(stored) != owner))
            {
                await _storage.ClearAsync();
                await _storage.SaveAsync(OwnerKey, Encoding.UTF8.GetBytes(owner));
            }

            var partitions = await _storage.LoadAsync(PartitionsKey) is { Length: > 0 } known ? JsonSerializer.Deserialize<List<string>>(known) ?? [] : [];
            if (partitions.FirstOrDefault() == manifest.Partition)
            {
                return;
            }

            partitions.Remove(manifest.Partition);
            partitions.Insert(0, manifest.Partition);
            foreach (var old in partitions.Skip(KeptPartitions))
            {
                await _storage.DeleteAsync(old + "/");
            }

            await _storage.SaveAsync(PartitionsKey, JsonSerializer.SerializeToUtf8Bytes(partitions.Take(KeptPartitions).ToList()));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Tidying the kept client entities failed");
        }
    }

    // the sets and the manifest go first: a query meanwhile starts a set of the new partition, none joins the ones being disposed
    private async Task DisposeSetsAsync()
    {
        _manifest = null;
        List<Task<IAsyncDisposable>> subscriptions = [.. _subscriptions];
        List<ClientEntitySet> sets = [.. _sets.Values];
        _subscriptions.Clear();
        _sets.Clear();
        foreach (var subscription in subscriptions)
        {
            await (await subscription).DisposeAsync();
        }

        foreach (var set in sets)
        {
            await set.DisposeAsync();
        }
    }
}
