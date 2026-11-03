using System.Reflection;
using System.Runtime.InteropServices;
using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed class VersionsStatus : ISystemStatusContributor
{
    public Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new SystemStatusSection("versions", "Versions",
    [
        new("Library", Version(typeof(Coworkee.Core.Modularity.CoworkeeModule).Assembly)),
        new("App", Assembly.GetEntryAssembly() is { } app ? $"{app.GetName().Name} {Version(app)}" : "-"),
        new(".NET", RuntimeInformation.FrameworkDescription),
        new("Operating system", RuntimeInformation.OSDescription),
    ]));

    private static string Version(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "-";
}
