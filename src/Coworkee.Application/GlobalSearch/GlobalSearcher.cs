using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Application.GlobalSearch;

public sealed class GlobalSearchOptions
{
    public const string Section = "Coworkee:GlobalSearch";

    /// <summary>A provider still searching after this time ends as timed out; the others are not held up.</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Hits per provider when the caller asks for no limit.</summary>
    public int DefaultLimit { get; set; } = 5;

    public int MaxLimit { get; set; } = 50;
}

/// <summary>Runs the providers the current user may use side by side and hands out their hits as they come.</summary>
public sealed class GlobalSearcher(
    IEnumerable<IGlobalSearchProvider> providers, IPermissionChecker permissions, IServiceScopeFactory scopes, IOptions<GlobalSearchOptions> options,
    ILogger<GlobalSearcher> logger)
{
    public async Task<IReadOnlyList<IGlobalSearchProvider>> AvailableAsync(CancellationToken cancellationToken)
    {
        var available = new List<IGlobalSearchProvider>();
        foreach (var provider in providers)
        {
            if (provider.Permission is null || await permissions.IsGrantedAsync(provider.Permission, cancellationToken))
            {
                available.Add(provider);
            }
        }

        return available;
    }

    /// <param name="types">Provider names to search in besides the ones the text names (<c>users:max</c>).</param>
    public async IAsyncEnumerable<GlobalSearchEvent> StreamAsync(
        string? text, IReadOnlyCollection<string>? types, int? limit, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var available = await AvailableAsync(cancellationToken);
        var query = GlobalSearchQuery.Parse(text, word => available.Any(p => GlobalSearchQuery.Names(word, p.Name, p.Group)));
        if (query.Text.Length == 0)
        {
            yield break;
        }

        var wanted = query.Types.Concat(types ?? []).ToList();
        var chosen = wanted.Count == 0 ? available : available.Where(p => wanted.Any(w => GlobalSearchQuery.Names(w, p.Name, p.Group))).ToList();
        var perProvider = Math.Clamp(limit ?? options.Value.DefaultLimit, 1, options.Value.MaxLimit);
        var channel = Channel.CreateUnbounded<GlobalSearchEvent>(new UnboundedChannelOptions { SingleReader = true });
        var runs = chosen.Select(p => RunAsync(p, query, perProvider, channel.Writer, cancellationToken)).ToList();
        _ = Task.WhenAll(runs).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);
        await foreach (var found in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return found;
        }
    }

    /// <summary>The whole search at once: hits best first, then how each provider ended.</summary>
    public async Task<GlobalSearchResult> SearchAsync(string? text, IReadOnlyCollection<string>? types, int? limit, CancellationToken cancellationToken)
    {
        var hits = new List<GlobalSearchHit>();
        var states = new List<GlobalSearchProviderStatus>();
        var groups = providers.ToDictionary(p => p.Name, p => p.Group);
        await foreach (var found in StreamAsync(text, types, limit, cancellationToken))
        {
            if (found.Hit is { } hit)
            {
                hits.Add(hit);
            }
            else if (found.State is { } state)
            {
                states.Add(new GlobalSearchProviderStatus(found.Provider, groups.GetValueOrDefault(found.Provider, found.Provider), state, hits.Count(h => h.Provider == found.Provider)));
            }
        }

        return new GlobalSearchResult([.. hits.OrderByDescending(h => h.Score)], states);
    }

    private async Task RunAsync(IGlobalSearchProvider provider, GlobalSearchQuery query, int limit, ChannelWriter<GlobalSearchEvent> writer, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.ProviderTimeout);
        var state = GlobalSearchProviderState.Done;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            await foreach (var hit in provider.SearchAsync(query, new GlobalSearchContext(scope.ServiceProvider, limit), timeout.Token).WithCancellation(timeout.Token))
            {
                if (hit.Id is { } id && !seen.Add(id))
                {
                    continue;
                }

                await writer.WriteAsync(new GlobalSearchEvent(provider.Name, hit with
                {
                    Provider = provider.Name,
                    Group = string.IsNullOrEmpty(hit.Group) ? provider.Group : hit.Group,
                    Icon = hit.Icon ?? provider.Icon,
                    Score = Math.Clamp(hit.Score, 0, 1) * provider.Weight,
                }), cancellationToken);
                if (++count == limit)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            state = GlobalSearchProviderState.TimedOut;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Global search provider {Provider} failed", provider.Name);
            state = GlobalSearchProviderState.Failed;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await writer.WriteAsync(new GlobalSearchEvent(provider.Name, State: state), CancellationToken.None);
        }
    }
}
