using System.Globalization;
using Nextended.Core.Facets;

namespace Coworkee.OData;

/// <summary>
/// The OData literals of facet options on every database: simple literals (quoted text and enum names, plain numbers, guids and dates).
/// Booleans read back as "1"/"0" (SQLite, SQL Server) are coerced by Nextended since 10.1.44; <see cref="Flag"/> still reads them for the option labels.
/// </summary>
internal sealed class FacetLiterals : IODataLiteralFormatter
{
    public static FacetBuilderOptions Options() => new() { BuildLiterals = true };

    public string ToLiteral(object? value, Type type) =>
        DefaultODataLiteralFormatter.ToSimpleLiteral(value, type);

    public string RangeClause(string field, object fromInclusive, object toExclusive, Type type) =>
        $"({field} ge {ToLiteral(Of(fromInclusive, type), type)} and {field} lt {ToLiteral(Of(toExclusive, type), type)})";

    // the date presets of a DateTime come as DateTimeOffsets, which do not convert to DateTime
    private static object Of(object value, Type type) => value is DateTimeOffset offset && type == typeof(DateTime) ? offset.UtcDateTime : value;

    /// <summary>A boolean as databases return it: "true", "True" or "1".</summary>
    public static bool Flag(string text) =>
        bool.TryParse(text, out var flag) ? flag : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number != 0;
}
