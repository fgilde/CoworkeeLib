namespace Coworkee.Contracts.DataFilters;

public static class DataFilterPermissions
{
    public const string GroupName = "DataFilters";

    /// <summary>Write the filters and links that apply to every organisation (system organisation only).</summary>
    public const string ManageSystem = "DataFilters.ManageSystem";

    /// <summary>Write the filters of the own organisation.</summary>
    public const string ManageTenant = "DataFilters.ManageTenant";

    /// <summary>Write the filters of groups of the own organisation.</summary>
    public const string ManageGroups = "DataFilters.ManageGroups";

    /// <summary>Write the filters of roles of the own organisation.</summary>
    public const string ManageRoles = "DataFilters.ManageRoles";

    /// <summary>See every row: no data filter applies. Granted to the system administrator role (which holds every permission).</summary>
    public const string Bypass = "DataFilters.Bypass";
}
