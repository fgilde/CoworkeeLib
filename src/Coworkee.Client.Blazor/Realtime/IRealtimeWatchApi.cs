using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Realtime;

/// <summary>The watches of sets another system writes (AddRealtimeWatch on the server): their state, and switching them for who may manage them.</summary>
public interface IRealtimeWatchApi
{
    Task<IReadOnlyList<RealtimeWatchDto>> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string entityType, bool enabled, CancellationToken cancellationToken = default);
}
