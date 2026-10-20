using System.Globalization;
using Coworkee.Application.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

public static class RecipientCultures
{
    /// <summary>Each reader's language: his own setting, else the one of his organisation, else the system's; none leaves the default.</summary>
    public static async Task<Dictionary<Guid, CultureInfo?>> ForAsync(CoworkeeDbContext db, IReadOnlyDictionary<Guid, Guid?> tenantOfUser, CancellationToken cancellationToken)
    {
        var userIds = tenantOfUser.Keys.ToList();
        var tenantIds = tenantOfUser.Values.OfType<Guid>().Distinct().ToList();
        var values = await db.Set<SettingValue>().AsNoTracking()
            .Where(v => v.Name == LocalizationSettings.Culture && v.Value != null && v.Value != string.Empty)
            .Where(v => v.Scope == SettingScope.Global
                        || (v.Scope == SettingScope.Tenant && tenantIds.Contains(v.ScopeKey!.Value))
                        || (v.Scope == SettingScope.User && userIds.Contains(v.ScopeKey!.Value)))
            .ToListAsync(cancellationToken);
        string? Of(SettingScope scope, Guid? key) => values.FirstOrDefault(v => v.Scope == scope && v.ScopeKey == key)?.Value;
        return tenantOfUser.ToDictionary(p => p.Key, p => Culture(
            Of(SettingScope.User, p.Key) ?? Of(SettingScope.Tenant, p.Value) ?? Of(SettingScope.Global, null)));
    }

    /// <summary>One reader's language as <see cref="ForAsync"/> finds it, else the default language of the organisation (Languages page included), else English.</summary>
    public static async Task<CultureInfo> OfAsync(CoworkeeDbContext db, Guid userId, Guid? tenantId, IEnumerable<IDefaultLanguage> defaults, CancellationToken cancellationToken) =>
        (await ForAsync(db, new Dictionary<Guid, Guid?> { [userId] = tenantId }, cancellationToken))[userId]
        ?? Culture(await defaults.GetAsync(cancellationToken))
        ?? CultureInfo.GetCultureInfo("en");

    private static CultureInfo? Culture(string? name)
    {
        try
        {
            return name is null ? null : CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
