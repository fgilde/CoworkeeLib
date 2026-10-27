using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Querying;

/// <param name="WeekIndex">SQL Server has no day of week independent of its settings: weeks are counted from a Monday instead.</param>
/// <param name="BinaryDate">A DateTimeOffset of a Coworkee DbContext on SQLite, stored as a number (DateTimeOffsetToBinaryConverter): its parts are computed from it.</param>
internal sealed record ReportGroup(ReportField Field, DateBucket Bucket, bool WeekIndex = false, bool BinaryDate = false);

internal sealed record ReportValue(AggregateFunction Function, ReportField? Field);

/// <summary>A query checked against the source: only known fields, functions that fit their kind, bounded sizes.</summary>
internal sealed record ReportPlan(ReportGroup? Group, ReportGroup? Split, IReadOnlyList<ReportValue> Values, ReportSortBy SortBy, bool Descending, int? Top)
{
    public const int MaxTop = 1000;

    /// <param name="granted">Permissions of the user among those navigations need.</param>
    /// <param name="binaryDates">DateTimeOffset columns are numbers (a Coworkee DbContext on SQLite).</param>
    public static Result<ReportPlan> Create(ReportSource source, ReportQueryRequest request, IReadOnlySet<string> granted, string provider = DatabaseProviders.Postgres, bool binaryDates = false)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var group = GroupOf(source, request.GroupBy, nameof(request.GroupBy), granted, errors, provider, binaryDates);
        var split = GroupOf(source, request.SplitBy, nameof(request.SplitBy), granted, errors, provider, binaryDates);
        if (split is not null && group is null)
        {
            errors[nameof(request.SplitBy)] = ["Series need a group by field."];
        }

        var aggregates = request.Aggregates is { Count: > 0 } given ? given : [new ReportAggregate()];
        if (aggregates.Count > ReportLimits.Aggregates)
        {
            errors[nameof(request.Aggregates)] = [$"At most {ReportLimits.Aggregates} values."];
        }

        var values = aggregates.Select((a, i) => Value(source, a, $"{nameof(request.Aggregates)}[{i}]", granted, errors)).ToList();
        if (request.Top is < 1 or > MaxTop)
        {
            errors[nameof(request.Top)] = [$"Top is between 1 and {MaxTop}."];
        }

        return errors.Count > 0
            ? Error.Validation(errors)
            : new ReportPlan(group, split, values!, request.SortBy, request.Descending, request.Top);
    }

    private static ReportGroup? GroupOf(ReportSource source, ReportGroupBy? by, string key, IReadOnlySet<string> granted, Dictionary<string, string[]> errors, string provider, bool binaryDates)
    {
        if (by is null)
        {
            return null;
        }

        if (Resolve(source, by.Field, key, granted, errors) is not { } field)
        {
            return null;
        }

        if (by.Bucket != DateBucket.None && field.Kind != ReportFieldKind.Date)
        {
            errors[key] = [$"Only date fields group by {by.Bucket.ToString().ToLowerInvariant()}."];
            return null;
        }

        // an exact point in time is no category; a day is
        var bucket = field.Kind == ReportFieldKind.Date && by.Bucket == DateBucket.None ? DateBucket.Day : by.Bucket;

        // EF's SQLite provider has no date parts of DateTimeOffset; a Coworkee DbContext stores it as a number the parts are computed from
        var offset = field.Kind == ReportFieldKind.Date && provider == DatabaseProviders.Sqlite && (Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType) == typeof(DateTimeOffset);
        if (offset && !binaryDates)
        {
            errors[key] = [$"SQLite cannot group {field.Path} (DateTimeOffset) of this database by date; use a DateTime or DateOnly field, or Postgres or SQL Server."];
            return null;
        }

        return new ReportGroup(field, bucket, bucket == DateBucket.Week && provider == DatabaseProviders.SqlServer, offset);
    }

    private static ReportValue? Value(ReportSource source, ReportAggregate aggregate, string key, IReadOnlySet<string> granted, Dictionary<string, string[]> errors)
    {
        if (aggregate.Function == AggregateFunction.Count)
        {
            return new ReportValue(AggregateFunction.Count, null);
        }

        if (Resolve(source, aggregate.Field, key, granted, errors) is not { } field)
        {
            return null;
        }

        if (aggregate.Function != AggregateFunction.DistinctCount && !field.IsNumeric)
        {
            errors[key] = [$"{aggregate.Function} needs a number field, {field.Path} is {field.Kind.ToString().ToLowerInvariant()}."];
            return null;
        }

        return new ReportValue(aggregate.Function, field);
    }

    private static ReportField? Resolve(ReportSource source, string? path, string key, IReadOnlySet<string> granted, Dictionary<string, string[]> errors)
    {
        if (source.Find(path) is { } field && field.Allowed(granted))
        {
            return field;
        }

        errors[key] = [$"{source.EntitySet} has no field '{path}'."];
        return null;
    }
}
