using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coworkee.Settings;

/// <summary>A place the service directory learns services from; register your own with <c>TryAddEnumerable</c>. Throw when the source cannot be asked, or take longer than three seconds: the directory leaves it out and reports it as unavailable.</summary>
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

    /// <summary>The service by name, case-insensitive; the first source in registration order that knows it wins, and the configured services are registered first. <c>"source:name"</c> (e.g. <c>aspireui:billing</c>) asks that source only. Null when no source knows it or the source is unavailable. Fast: the health of a configured service is not probed and stays <see cref="ServiceHealth.Unknown"/>.</summary>
    Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken);
}

internal sealed partial class ServiceDirectory(IEnumerable<IServiceSource> sources, ILogger<ServiceDirectory> logger) : IServiceDirectory
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<ServiceDirectoryResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(sources.Select(async source => (source.Name, Services: await AskAsync(source, (s, token) => s.GetAsync(token), cancellationToken))));
        return new ServiceDirectoryResult(
            results.SelectMany(r => (r.Services ?? []).Select(s => s with { Source = r.Name })).ToList(),
            results.Where(r => r.Services is null).Select(r => r.Name).ToList());
    }

    public async Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken)
    {
        var plain = name;
        var candidates = sources.ToList();
        if (name.Split(':', 2) is [var qualifier, var rest] && candidates.Where(s => string.Equals(s.Name, qualifier, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: > 0 } qualified)
        {
            (plain, candidates) = (rest, qualified);
        }

        foreach (var source in candidates)
        {
            if (await AskAsync(source, (s, token) => s.FindAsync(plain, token), cancellationToken) is { } found)
            {
                return found with { Source = source.Name };
            }
        }

        return null;
    }

    private async Task<T?> AskAsync<T>(IServiceSource source, Func<IServiceSource, CancellationToken, Task<T>> ask, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            return await ask(source, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (ServiceSourceUnavailableException) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnavailableAlreadyReported(source.Name);
            return default;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(source.Name, exception.GetType().Name);
            return default;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The service source {Source} is unavailable ({Reason})")]
    private partial void LogUnavailable(string source, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The service source {Source} is unavailable, the source logged why")]
    private partial void LogUnavailableAlreadyReported(string source);
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
