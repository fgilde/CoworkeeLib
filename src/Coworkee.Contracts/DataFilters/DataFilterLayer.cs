namespace Coworkee.Contracts.DataFilters;

/// <summary>Who a data filter applies to: every organisation, one organisation, the members of a group or the holders of a role.</summary>
public enum DataFilterLayer
{
    System,
    Tenant,
    Group,
    Role,
}
