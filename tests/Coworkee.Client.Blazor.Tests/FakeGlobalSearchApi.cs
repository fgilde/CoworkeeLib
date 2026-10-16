using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Coworkee.Client.Blazor.Search;
using Coworkee.Contracts.Search;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Streams what the test pushes, when it pushes it; <see cref="Complete"/> ends the stream.</summary>
public sealed class FakeGlobalSearchApi : IGlobalSearchApi
{
    private Channel<GlobalSearchEvent> _events = Channel.CreateUnbounded<GlobalSearchEvent>();

    public List<string> Queries { get; } = [];

    public List<GlobalSearchProviderDto> Providers { get; } = [];

    public void Push(GlobalSearchHit hit) => _events.Writer.TryWrite(new GlobalSearchEvent(hit.Provider, hit));

    public void Complete()
    {
        _events.Writer.TryComplete();
        _events = Channel.CreateUnbounded<GlobalSearchEvent>();
    }

    public async IAsyncEnumerable<GlobalSearchEvent> StreamAsync(string text, IReadOnlyCollection<string>? types = null, int? limit = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Queries.Add(text);
        await foreach (var found in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return found;
        }
    }

    public Task<IReadOnlyList<GlobalSearchProviderDto>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GlobalSearchProviderDto>>(Providers);
}
