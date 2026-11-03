using Coworkee.OData;

namespace Coworkee.Reporting.Querying;

/// <summary>
/// A registered entity set or app source with the fields reports may use; a field behind a navigation needs the permission of its set too,
/// and reads only the rows its row filters show (<see cref="ODataNavigationScope"/>, applied when the query runs).
/// </summary>
/// <param name="Computed">The app's source, null for an entity set.</param>
internal sealed record ReportSource(string EntitySet, Type RowType, string? Permission, string Label, IReadOnlyList<ReportField> Fields, Sources.ReportSourceRegistration? Computed = null)
{
    public ReportField? Find(string? path) => Fields.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
}
