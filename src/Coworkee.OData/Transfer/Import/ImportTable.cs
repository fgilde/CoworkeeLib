namespace Coworkee.OData.Transfer.Import;

/// <summary>
/// The cells of an uploaded file as text. <see cref="Typed"/> files (Excel, JSON) write numbers and dates invariant,
/// text files in the language of whoever wrote them.
/// </summary>
public sealed record ImportTable(IReadOnlyList<string> Headers, IReadOnlyList<string?[]> Rows, bool Typed, bool HasHeader, string? Delimiter = null, string? Encoding = null)
{
    /// <summary>The line or sheet row a data row came from, as the user sees it in the file.</summary>
    public int RowNumber(int index) => index + (HasHeader ? 2 : 1);
}
