namespace Coworkee.Client.Blazor.Data;

public sealed record SavedTableView(string Name, DataTableState State)
{
    /// <summary>Opens with the table when its address carries no state of its own.</summary>
    public bool IsDefault { get; init; }
}
