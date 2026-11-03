using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed record FetchedApps(ServiceDto[] Services, bool Unavailable)
{
    public static FetchedApps Failed { get; } = new([], true);
}
