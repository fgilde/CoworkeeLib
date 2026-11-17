using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Application.GlobalSearch;

/// <summary>Runs the providers the current user may use side by side and hands out their hits as they come.</summary>
public sealed class GlobalSearcher(
    IEnumerable<IGlobalSearchProvider> providers, IServiceProvider services, IServiceScopeFactory scopes, IOptions<GlobalSearchOptions> options,
    ILogger<GlobalSearcher> logger)
{
    public async Task<IReadOnlyList<IGlobalSearchProvider>> AvailableAsync(CancellationToken cancellationToken)
    {
        // resolved on use like the authorization middleware: hosts without the identity module still build
        var permissions = services.GetRequiredService<IPermissionChecker>();
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
    public IAsyncEnumerable<GlobalSearchEvent> StreamAsync(string? text, IReadOnlyCollection<string>? types, int? limit, CancellationToken cancellationToken) =>
        StreamAsync(text, types, limit, 0, false, cancellationToken);

    /// <summary>
    /// A page of each provider's hits: the first <paramref name="skip"/> are left out (a results page that pages through one type), and with
    /// <paramref name="totals"/> the end of each provider tells how many records match in all (<see cref="GlobalSearchEvent.Total"/>, null where a
    /// provider cannot count).
    /// </summary>
    /// <param name="skip">Hits of each provider to leave out, at most <see cref="GlobalSearchOptions.MaxSkip"/>.</param>
    public async IAsyncEnumerable<GlobalSearchEvent> StreamAsync(
        string? text, IReadOnlyCollection<string>? types, int? limit, int skip, bool totals, [EnumeratorCancellation] CancellationToken cancellationToken)
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
        var page = new Page(Math.Clamp(skip, 0, options.Value.MaxSkip), perProvider, totals);
        var channel = Channel.CreateUnbounded<GlobalSearchEvent>(new UnboundedChannelOptions { SingleReader = true });
        var runs = chosen.Select(p => RunAsync(p, query, page, channel.Writer, cancellationToken)).ToList();
        _ = Task.WhenAll(runs).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);
        await foreach (var found in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return found;
        }
    }

    /// <summary>The whole search at once: hits best first, then how each provider ended.</summary>
    public Task<GlobalSearchResult> SearchAsync(string? text, IReadOnlyCollection<string>? types, int? limit, CancellationToken cancellationToken) =>
        SearchAsync(text, types, limit, 0, false, cancellationToken);

    /// <summary>A page of the search at once (see <see cref="StreamAsync(string?, IReadOnlyCollection{string}?, int?, int, bool, CancellationToken)"/>).</summary>
    public async Task<GlobalSearchResult> SearchAsync(
        string? text, IReadOnlyCollection<string>? types, int? limit, int skip, bool totals, CancellationToken cancellationToken)
    {
        var hits = new List<GlobalSearchHit>();
        var states = new List<GlobalSearchProviderStatus>();
        var groups = providers.DistinctBy(p => p.Name).ToDictionary(p => p.Name, p => p.Group);
        await foreach (var found in StreamAsync(text, types, limit, skip, totals, cancellationToken))
        {
            if (found.Hit is { } hit)
            {
                hits.Add(hit);
            }
            else if (found.State is { } state)
            {
                states.Add(new GlobalSearchProviderStatus(found.Provider, groups.GetValueOrDefault(found.Provider, found.Provider), state, hits.Count(h => h.Provider == found.Provider))
                {
                    Total = found.Total,
                });
            }
        }

        return new GlobalSearchResult([.. hits.OrderByDescending(h => h.Score)], states);
    }

    private sealed record Page(int Skip, int Take, bool Totals);

    // a page beyond the first asks the provider for the hits before it too and leaves them out: providers stay simple, deep pages cost more
    // ponytail: skip counts unique hits of the provider's order; providers that order differently between calls page inconsistently
    private async Task RunAsync(IGlobalSearchProvider provider, GlobalSearchQuery query, Page page, ChannelWriter<GlobalSearchEvent> writer, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.ProviderTimeout);
        var state = GlobalSearchProviderState.Done;
        long? total = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = new GlobalSearchContext(scope.ServiceProvider, page.Skip + page.Take);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            var skipped = 0;
            await foreach (var hit in provider.SearchAsync(query, context, timeout.Token).WithCancellation(timeout.Token))
            {
                if (hit.Id is { } id && !seen.Add(id))
                {
                    continue;
                }

                if (skipped < page.Skip)
                {
                    skipped++;
                    continue;
                }

                await writer.WriteAsync(new GlobalSearchEvent(provider.Name, hit with
                {
                    Provider = provider.Name,
                    Group = string.IsNullOrEmpty(hit.Group) ? provider.Group : hit.Group,
                    Icon = hit.Icon ?? provider.Icon,
                    Score = Math.Clamp(hit.Score, 0, 1) * provider.Weight,
                }), cancellationToken);
                if (++count == page.Take)
                {
                    break;
                }
            }

            // after the hits, in the same scope: one DbContext runs one query at a time
            total = page.Totals ? await provider.CountAsync(query, context, timeout.Token) : null;
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
            await writer.WriteAsync(new GlobalSearchEvent(provider.Name, State: state) { Total = total }, CancellationToken.None);
        }
    }
}
