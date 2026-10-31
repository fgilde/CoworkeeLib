namespace Coworkee.Client.Blazor.Data;

/// <summary>Rows the client keeps of what the server shows (client entities); dropped when that changes, e.g. after a new group selection.</summary>
public interface IClientDataCache
{
    Task ClearAsync();

    /// <summary>The rows the user sees changed as a whole (a new group selection): copies of the old scope must not answer any more. The default drops everything.</summary>
    Task ScopeChangedAsync() => ClearAsync();
}
