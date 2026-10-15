using Coworkee.Application.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Languages;

/// <summary>The language setting of the organisation, else of the system, else the default language of the Languages page.</summary>
internal sealed class DefaultLanguage(ISettingProvider settings, CoworkeeDbContext db) : IDefaultLanguage
{
    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        foreach (var scope in new[] { SettingScope.Tenant, SettingScope.Global })
        {
            if (await settings.GetForScopeAsync(LocalizationSettings.Culture, scope, storedOnly: true, cancellationToken) is { Length: > 0 } culture)
            {
                return culture;
            }
        }

        return await db.Set<Language>().AsNoTracking().Where(l => l.IsDefault).Select(l => l.Culture).FirstOrDefaultAsync(cancellationToken);
    }
}
