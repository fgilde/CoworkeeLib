using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;

namespace Coworkee.Realtime;

internal sealed class RealtimeStatus(RealtimeConnections connections) : ISystemStatusContributor
{
    public Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new SystemStatusSection("realtime", "Realtime",
    [
        new("Connections on this instance", connections.Count.ToString()),
        new("Users connected to this instance", connections.Users.ToString()),
    ], Order: 4));
}
