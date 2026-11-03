using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

/// <summary>All services the app knows: the configured ones and those of every other source. Backend code, no user check.</summary>
public interface IServiceDirectory
{
    /// <summary>Every service of every source, the configured ones with their live health; a source that fails is named in <c>UnavailableSources</c>.</summary>
    Task<ServiceDirectoryResult> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The service by name, case-insensitive; the first source in registration order that knows it wins, and the configured services are registered first. <c>"source:name"</c> (e.g. <c>aspireui:billing</c>) asks that source only. Null when no source knows it or the source is unavailable. Fast: the health of a configured service is not probed and stays <see cref="ServiceHealth.Unknown"/>.</summary>
    Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken);
}
