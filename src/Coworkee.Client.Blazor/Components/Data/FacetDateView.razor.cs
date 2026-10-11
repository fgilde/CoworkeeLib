using System.Globalization;
using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// A date facet: presets (today, this week, last 30 days, ...) that stay relative in saved views, a histogram whose bars pick a day,
/// month or year, and a custom range of two dates.
/// </summary>
public partial class FacetDateView
{
    private DateTime? _from;
    private DateTime? _to;

    private SelectedFacet? Chosen => Selection.Of(Group.Key).FirstOrDefault();

    private string Path => Group.ValuePath ?? Group.Field;

    private void Preset(string key, string label)
    {
        if (Chosen?.Preset == key)
        {
            Selection.Set(Group, null, null);
            return;
        }

        Selection.Set(Group, label, FacetPresets.ODataFor(key, Path, Selection.Now()), key);
    }

    private Task PickAsync(FacetOptionDto bucket)
    {
        Selection.Set(Group, Chosen?.OData == bucket.OData ? null : Show(bucket), bucket.OData);
        return Task.CompletedTask;
    }

    private void Custom()
    {
        var rule = new QueryRule { Field = Path };
        (rule.Operator, rule.Value, rule.Value2) = (_from, _to) switch
        {
            ({ } from, { } to) => (QueryOperator.Between, QueryDates.Format(from), QueryDates.Format(to)),
            ({ } from, null) => (QueryOperator.GreaterThanOrEqual, QueryDates.Format(from), null),
            (null, { } to) => (QueryOperator.LessThanOrEqual, QueryDates.Format(to), null),
            _ => (QueryOperator.Equal, null, null),
        };
        var odata = ODataQueryTranslator.Rule(rule, new Dictionary<string, QueryField> { [Path] = new(Path, Path, QueryFieldType.DateTime) }, Selection.Now());
        Selection.Set(Group, $"{_from?.ToString("d", CultureInfo.CurrentCulture)} – {_to?.ToString("d", CultureInfo.CurrentCulture)}".Trim(), odata);
    }

    private string BucketTitle(FacetOptionDto bucket) => $"{Show(bucket)}: {Count(bucket.Count)}";

    /// <summary>A bucket as its unit reads: a day, "Oct 2026" or "2026".</summary>
    private static string Show(FacetOptionDto bucket)
    {
        if (!DateTime.TryParse(bucket.Meta?.GetValueOrDefault("from"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
        {
            return bucket.Label;
        }

        return bucket.Meta?.GetValueOrDefault("unit") switch
        {
            "day" => from.ToString("d", CultureInfo.CurrentCulture),
            "month" => from.ToString("MMM yyyy", CultureInfo.CurrentCulture),
            _ => from.ToString("yyyy", CultureInfo.CurrentCulture),
        };
    }
}
