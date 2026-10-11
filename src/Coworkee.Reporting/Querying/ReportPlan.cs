using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Querying;

internal sealed record ReportGroup(ReportField Field, DateBucket Bucket);

internal sealed record ReportValue(AggregateFunction Function, ReportField? Field);

/// <summary>A query checked against the source: only known fields, functions that fit their kind, bounded sizes.</summary>
internal sealed record ReportPlan(ReportGroup? Group, ReportGroup? Split, IReadOnlyList<ReportValue> Values, ReportSortBy SortBy, bool Descending, int? Top)
{
    public const int MaxTop = 1000;

    /// <param name="granted">Permissions of the user among those navigations need.</param>
    public static Result<ReportPlan> Create(ReportSource source, ReportQueryRequest request, IReadOnlySet<string> granted)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var group = GroupOf(source, request.GroupBy, nameof(request.GroupBy), granted, errors);
        var split = GroupOf(source, request.SplitBy, nameof(request.SplitBy), granted, errors);
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

    private static ReportGroup? GroupOf(ReportSource source, ReportGroupBy? by, string key, IReadOnlySet<string> granted, Dictionary<string, string[]> errors)
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
        return new ReportGroup(field, field.Kind == ReportFieldKind.Date && by.Bucket == DateBucket.None ? DateBucket.Day : by.Bucket);
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
        if (source.Find(path) is { } field && (field.Permission is null || granted.Contains(field.Permission)))
        {
            return field;
        }

        errors[key] = [$"{source.Registration.EntitySet} has no field '{path}'."];
        return null;
    }
}
