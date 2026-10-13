using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;

namespace Coworkee.Settings;

/// <summary>A place the service directory learns services from; register your own with <c>TryAddEnumerable</c>.</summary>
public interface IServiceSource
{
    string Name { get; }

    Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>All services the app knows: configured ones and those of every other source. Backend code, no user check.</summary>
public interface IServiceDirectory
{
    /// <summary>Every service of every source, the configured ones with their live health.</summary>
    Task<IReadOnlyList<ServiceDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The service by name, case-insensitive; a configured service wins over another source. Fast: the health is not probed, so <c>Health</c> of a configured service is not meaningful.</summary>
    Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken);
}

internal sealed class ServiceDirectory(IEnumerable<IServiceSource> sources) : IServiceDirectory
{
    private readonly IServiceSource[] _sources = [.. sources];

    public async Task<IReadOnlyList<ServiceDto>> GetAllAsync(CancellationToken cancellationToken) =>
        (await Task.WhenAll(_sources.Select(s => s.GetAsync(cancellationToken)))).SelectMany(list => list).ToList();

    public async Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken)
    {
        var configured = _sources.OfType<ConfiguredServiceSource>().Single();
        if (configured.Find(name) is { } service)
        {
            return service;
        }

        foreach (var source in _sources.Where(s => s != configured))
        {
            var found = (await source.GetAsync(cancellationToken)).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetServices : IQuery<Result<IReadOnlyList<ServiceDto>>>;

internal sealed class ServiceDirectoryHandler(IServiceDirectory directory, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetServices, Result<IReadOnlyList<ServiceDto>>>
{
    private static readonly Error SystemOnly = Error.Forbidden("services.system_only", "The services of the app are shown in the system organisation.");

    public async Task<Result<IReadOnlyList<ServiceDto>>> HandleAsync(GetServices query, CancellationToken cancellationToken)
    {
        if (!await currentUser.IsInSystemTenantAsync(services, cancellationToken))
        {
            return SystemOnly;
        }

        return Result<IReadOnlyList<ServiceDto>>.Success(await directory.GetAllAsync(cancellationToken));
    }
}
