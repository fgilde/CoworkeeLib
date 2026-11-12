namespace Coworkee.Client.Blazor.Data;

/// <summary>Rows the client keeps of what the server shows (client entities); dropped when that changes, e.g. after a new group selection.</summary>
public interface IClientDataCache
{
    Task ClearAsync();

    /// <summary>The rows the user sees changed as a whole (a new group selection): copies of the old scope must not answer any more. The default drops everything.</summary>
    Task ScopeChangedAsync() => ClearAsync();

    /// <summary>Queries of <paramref name="entitySet"/> run in the browser now (the copy is complete and not switched to the server), e.g. for a search box that need not wait.</summary>
    bool AnswersLocally(string entitySet) => false;

    /// <summary>The sets <see cref="AnswersLocally"/> is true for right now; the assistant reads them here instead of on the server.</summary>
    IReadOnlyList<string> LocalSets => [];

    /// <summary>
    /// <paramref name="query"/> (filter, order, top, skip and the fields of <see cref="ODataQuery.Select"/>) answered from the local copy of
    /// <paramref name="entitySet"/> without asking the server; null when it cannot be answered here: the set is not local or switched to the server, the
    /// filter is beyond the local engine or names a field the copy does not have, or the answer would hold fewer fields than the server's (no select and a
    /// row type without every field the server returns).
    /// </summary>
    Task<LocalRows?> QueryLocallyAsync(string entitySet, ODataQuery query, CancellationToken cancellationToken = default) => Task.FromResult<LocalRows?>(null);
}
