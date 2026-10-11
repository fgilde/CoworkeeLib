using System.Globalization;
using Nextended.Core.Facets;

namespace Coworkee.OData;

/// <summary>
/// The OData literals of facet options on every database. The facet builder reads grouped values back as text, and SQLite and
/// SQL Server answer booleans as "1"/"0", which Nextended's simple literals cannot turn into <c>true</c>/<c>false</c>
/// (fixed in its next release); everything else stays a simple literal: quoted text and enum names, plain numbers, guids and dates.
/// </summary>
internal sealed class FacetLiterals : IODataLiteralFormatter
{
    public static FacetBuilderOptions Options() => new() { BuildLiterals = true };

    public string ToLiteral(object? value, Type type) =>
        DefaultODataLiteralFormatter.ToSimpleLiteral(value is string text && (Nullable.GetUnderlyingType(type) ?? type) == typeof(bool) ? Flag(text) : value, type);

    public string RangeClause(string field, object fromInclusive, object toExclusive, Type type) =>
        $"({field} ge {ToLiteral(fromInclusive, type)} and {field} lt {ToLiteral(toExclusive, type)})";

    /// <summary>A boolean as databases return it: "true", "True" or "1".</summary>
    public static bool Flag(string text) =>
        bool.TryParse(text, out var flag) ? flag : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number != 0;
}
