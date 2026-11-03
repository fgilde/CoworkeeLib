using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

/// <summary>A place the service directory learns services from; register your own with <c>TryAddEnumerable</c>. Throw when the source cannot be asked, or take longer than ten seconds: the directory leaves it out and reports it as unavailable.</summary>
public interface IServiceSource
{
    string Name { get; }

    Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken);

    /// <summary>The service by name, case-insensitive; override when it can be answered cheaper than by listing all.</summary>
    async Task<ServiceDto?> FindAsync(string name, CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
