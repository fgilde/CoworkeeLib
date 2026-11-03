namespace Coworkee.Client.Blazor.Data;

/// <summary>Named table states per table; replace it in DI to keep them somewhere else.</summary>
public interface ITableViewStore
{
    Task<IReadOnlyList<SavedTableView>> GetAsync(string table);

    /// <summary>Adds the view or replaces the one with its name; a default view takes the default from the others.</summary>
    Task<IReadOnlyList<SavedTableView>> SaveAsync(string table, SavedTableView view);

    Task<IReadOnlyList<SavedTableView>> DeleteAsync(string table, string name);
}
