using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Logging;

namespace Coworkee.Settings;

internal sealed partial class ServiceDirectory(IEnumerable<IServiceSource> sources, TimeProvider time, ILogger<ServiceDirectory> logger) : IServiceDirectory
{
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
        using var timeout = ServiceSourceTimeouts.Limit(ServiceSourceTimeouts.Guard, time, cancellationToken);
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
