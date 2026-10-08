using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Localization.Texts;

/// <summary>Texts of a culture: module resources, then the edits of the administrators.</summary>
public sealed class TextStore(CoworkeeDbContext db, LocalizationResources resources, HybridCache cache)
{
    public const string CacheTag = "coworkee:localization";

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string culture, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync($"coworkee:texts:{culture.ToLowerInvariant()}", async token => await LoadAsync(culture, token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) }, [CacheTag], cancellationToken);

    public Task InvalidateAsync(CancellationToken cancellationToken) => cache.RemoveByTagAsync(CacheTag, cancellationToken).AsTask();

    private async Task<Dictionary<string, string>> LoadAsync(string culture, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<string, string>(resources.For(culture), StringComparer.Ordinal);
        var chain = LocalizationResources.CultureChain(culture).ToList();
        var edits = await db.Set<Translation>().AsNoTracking().Where(t => chain.Contains(t.Culture)).ToListAsync(cancellationToken);
        foreach (var edit in edits.OrderBy(t => t.Culture.Length))
        {
            texts[edit.Key] = edit.Value;
        }

        return texts;
    }
}
