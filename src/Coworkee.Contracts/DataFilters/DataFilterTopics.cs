namespace Coworkee.Contracts.DataFilters;

/// <summary>
/// Topics that say only "the data filters changed" (a payload-free signal: no entity, id, property or actor), so open tables reload, client copies
/// resynchronise and admin views load again. Every client follows both.
/// </summary>
public static class DataFilterTopics
{
    /// <summary>Tenant, group and role filters of the user's organisation changed.</summary>
    public const string TenantChanged = "tenant:data-filters";

    /// <summary>System filters or links changed: they apply to every organisation.</summary>
    public const string Changed = "global:data-filters";
}
