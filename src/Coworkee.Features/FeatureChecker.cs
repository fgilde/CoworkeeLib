using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Features;

public interface IFeatureChecker
{
    /// <summary>The value for the current tenant: its override, else its edition's value, else the default.</summary>
    Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default);
}

internal sealed class FeatureChecker(CoworkeeDbContext db, ICurrentUser currentUser, IFeatureDefinitionManager definitions, HybridCache cache) : IFeatureChecker
{
    internal const string CacheTag = "coworkee:features";

    public async Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default)
    {
        var definition = definitions.Find(name) ?? throw new ArgumentException($"Feature '{name}' is not defined.", nameof(name));
        return (await LoadAsync(cancellationToken)).GetValueOrDefault(name) ?? definition.DefaultValue;
    }

    public async Task<bool> IsEnabledAsync(string name, CancellationToken cancellationToken = default) =>
        bool.TryParse(await GetValueAsync(name, cancellationToken), out var enabled) && enabled;

    public async Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var values = await LoadAsync(cancellationToken);
        return definitions.All.ToDictionary(d => d.Name, d => values.GetValueOrDefault(d.Name) ?? d.DefaultValue, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return [];
        }

        return await cache.GetOrCreateAsync(
            $"{CacheTag}:{tenantId}",
            async token =>
            {
                var set = await db.Set<TenantFeatureSet>().AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, token);
                var values = set?.EditionId is { } editionId
                    ? await db.Set<Edition>().AsNoTracking().Where(e => e.Id == editionId).Select(e => e.Values).SingleOrDefaultAsync(token) ?? []
                    : [];
                foreach (var (name, value) in set?.Overrides ?? [])
                {
                    values[name] = value;
                }

                return values;
            },
            tags: [CacheTag],
            cancellationToken: cancellationToken);
    }
}
