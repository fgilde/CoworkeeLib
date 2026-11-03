using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

public interface IODataClient
{
    Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The groups of the rows that match <paramref name="filter"/>: each combination of the properties <paramref name="by"/> (paths of <typeparamref name="T"/>
    /// with '.') with its count. Clients that cannot group throw <see cref="NotSupportedException"/>; decorators forward the call.
    /// </summary>
    Task<IReadOnlyList<ODataGroup>> GroupAsync<T>(string entitySet, string? filter, IReadOnlyList<string> by, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not group.");
}
