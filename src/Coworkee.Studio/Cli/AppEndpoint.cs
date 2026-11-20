using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.Cli;

public sealed record AppEndpoint(string Name, string Url)
{
    /// <summary>The addresses of a deployment record, the Aspire dashboard first under its own name.</summary>
    public static IReadOnlyList<AppEndpoint> Of(IReadOnlyDictionary<string, string> urls) =>
    [
        .. urls.OrderBy(u => u.Key != Deployments.DashboardKey)
            .Select(u => new AppEndpoint(u.Key == Deployments.DashboardKey ? L.Get("Aspire dashboard") : u.Key, u.Value)),
    ];
}
