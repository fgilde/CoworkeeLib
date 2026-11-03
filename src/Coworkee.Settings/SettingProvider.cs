using System.Globalization;
using Coworkee.Application.Caching;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

internal sealed class SettingProvider(
    ICurrentUser currentUser,
    ISettingDefinitionManager definitions,
    HybridCache cache,
    SettingProtector protector,
    IServiceProvider services) : ISettingProvider
{
    internal const string CacheTag = "coworkee:settings";

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var definition = definitions.Find(name) ?? throw new ArgumentException($"Setting '{name}' is not defined.", nameof(name));
        return Resolve(definition, await LoadAsync(cancellationToken));
    }

    public async Task<T> GetAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var value = await GetAsync(name, cancellationToken);
        return value is null ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    public async Task<string?> GetForScopeAsync(string name, SettingScope scope, bool storedOnly = false, CancellationToken cancellationToken = default)
    {
        var definition = definitions.Find(name) ?? throw new ArgumentException($"Setting '{name}' is not defined.", nameof(name));
        return Find(definition, await LoadAsync(cancellationToken), scope) is { } row ? Read(definition, row) : storedOnly ? null : definition.DefaultValue;
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetClientValuesAsync(CancellationToken cancellationToken = default)
    {
        var stored = await LoadAsync(cancellationToken);
        return definitions.All.Where(d => d.IsClientVisible).ToDictionary(d => d.Name, d => Resolve(d, stored), StringComparer.Ordinal);
    }

    private string? Resolve(SettingDefinition definition, IReadOnlyList<StoredSetting> stored) =>
        Resolve(definition, stored, SettingScope.User, SettingScope.Tenant, SettingScope.Global);

    private string? Resolve(SettingDefinition definition, IReadOnlyList<StoredSetting> stored, params SettingScope[] scopes) =>
        Find(definition, stored, scopes) is { } row ? Read(definition, row) : definition.DefaultValue;

    private static StoredSetting? Find(SettingDefinition definition, IReadOnlyList<StoredSetting> stored, params SettingScope[] scopes) =>
        scopes.Where(definition.Scopes.Contains).Select(scope => stored.FirstOrDefault(s => s.Name == definition.Name && s.Scope == scope)).FirstOrDefault(row => row is not null);

    private string? Read(SettingDefinition definition, StoredSetting row) => definition.IsEncrypted ? protector.Unprotect(row.Value) : row.Value;

    private async Task<IReadOnlyList<StoredSetting>> LoadAsync(CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;
        return await cache.GetOrCreateInScopeAsync(
            services,
            new ImpersonatedUser(userId, tenantId),
            $"coworkee:settings:{tenantId}:{userId}",
            async (scope, token) => await scope.GetRequiredService<CoworkeeDbContext>().Set<SettingValue>().AsNoTracking()
                .Where(s => (s.Scope == SettingScope.Global && s.ScopeKey == null)
                    || (s.Scope == SettingScope.Tenant && s.ScopeKey == tenantId && tenantId != null)
                    || (s.Scope == SettingScope.User && s.ScopeKey == userId && userId != null))
                .Select(s => new StoredSetting(s.Name, s.Scope, s.Value))
                .ToListAsync(token),
            tags: [CacheTag],
            cancellationToken: cancellationToken);
    }
}
