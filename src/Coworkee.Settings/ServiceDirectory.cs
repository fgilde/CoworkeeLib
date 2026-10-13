using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coworkee.Settings;

/// <summary>A place the service directory learns services from; register your own with <c>TryAddEnumerable</c>. Throw when the source cannot be asked: the directory leaves it out and reports it as unavailable.</summary>
public interface IServiceSource
{
    string Name { get; }

    Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken);

    /// <summary>The service by name, case-insensitive; override when it can be answered cheaper than by listing all.</summary>
    async Task<ServiceDto?> FindAsync(string name, CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>All services the app knows: the configured ones and those of every other source. Backend code, no user check.</summary>
public interface IServiceDirectory
{
    /// <summary>Every service of every source, the configured ones with their live health; a source that fails is named in <c>UnavailableSources</c>.</summary>
    Task<ServiceDirectoryResult> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The service by name, case-insensitive; the first source in registration order that knows it wins, and the configured services are registered first. Fast: the health of a configured service is not probed and stays <see cref="ServiceHealth.Unknown"/>.</summary>
    Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken);
}

internal sealed partial class ServiceDirectory(IEnumerable<IServiceSource> sources, ILogger<ServiceDirectory> logger) : IServiceDirectory
{
    public async Task<ServiceDirectoryResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(sources.Select(async source => (source.Name, Services: await AskAsync(source, s => s.GetAsync(cancellationToken), cancellationToken))));
        return new ServiceDirectoryResult(
            results.SelectMany(r => r.Services ?? []).ToList(),
            results.Where(r => r.Services is null).Select(r => r.Name).ToList());
    }

    public async Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var found = await AskAsync(source, s => s.FindAsync(name, cancellationToken), cancellationToken);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async Task<T?> AskAsync<T>(IServiceSource source, Func<IServiceSource, Task<T>> ask, CancellationToken cancellationToken)
    {
        try
        {
            return await ask(source);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(source.Name, exception.GetType().Name);
            return default;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The service source {Source} is unavailable ({Reason})")]
    private partial void LogUnavailable(string source, string reason);
}

[RequiresPermission(SettingsPermissions.Manage)]
public sealed record GetServices : IQuery<Result<ServiceDirectoryResult>>;

internal sealed class ServiceDirectoryHandler(IServiceDirectory directory, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetServices, Result<ServiceDirectoryResult>>
{
    private static readonly Error SystemOnly = Error.Forbidden("services.system_only", "The services of the app are shown in the system organisation.");

    public async Task<Result<ServiceDirectoryResult>> HandleAsync(GetServices query, CancellationToken cancellationToken)
    {
        if (!await currentUser.IsInSystemTenantAsync(services, cancellationToken))
        {
            return SystemOnly;
        }

        return await directory.GetAllAsync(cancellationToken);
    }
}
