using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

public interface IODataClient
{
    Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default);

    /// <summary>All rows matching filter, search and order as an Excel workbook.</summary>
    Task<byte[]> ExportAsync(string entitySet, ODataQuery query, CancellationToken cancellationToken = default);

    Task<ImportResult> ImportAsync(string entitySet, Stream workbook, string fileName, CancellationToken cancellationToken = default);
}
