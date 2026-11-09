using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

public interface ISettingProvider
{
    /// <summary>The hybrid cache tag a saved setting clears (on every instance with Redis); values derived from settings cached under it follow every change.</summary>
    const string CacheTag = "coworkee:settings";

    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);

    Task<T> GetAsync<T>(string name, CancellationToken cancellationToken = default);

    /// <summary>The value stored at exactly this scope (of the current tenant for <see cref="SettingScope.Tenant"/>), else the default (null with <paramref name="storedOnly"/> when nothing is stored); no fallback to other scopes.</summary>
    Task<string?> GetForScopeAsync(string name, SettingScope scope, bool storedOnly = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string?>> GetClientValuesAsync(CancellationToken cancellationToken = default);
}
