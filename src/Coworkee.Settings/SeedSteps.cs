using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

public static class SeedSteps
{
    /// <summary>
    /// True the first time a one-time seed step is claimed for the tenant (null: host); the marker is added and stored by the seed's save.
    /// False when a marker exists, saved or still unsaved; its value does not matter.
    /// </summary>
    public static async Task<bool> ClaimOnceAsync(this CoworkeeDbContext db, Guid? tenantId, string name, CancellationToken cancellationToken)
    {
        var scope = tenantId is null ? SettingScope.Global : SettingScope.Tenant;
        var values = db.Set<SettingValue>();
        if (values.Local.Any(v => v.Name == name && v.Scope == scope && v.ScopeKey == tenantId)
            || await values.IgnoreQueryFilters().AnyAsync(v => v.Name == name && v.Scope == scope && v.ScopeKey == tenantId, cancellationToken))
        {
            return false;
        }

        values.Add(new SettingValue { Name = name, Scope = scope, ScopeKey = tenantId, Value = "done" });
        return true;
    }
}
