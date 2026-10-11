using System.Globalization;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// A number facet as a histogram over a two-thumb slider with "from" and "to" fields. The choice is one range,
/// <c>(Path ge from and Path le to)</c>; the whole range means no choice.
/// </summary>
public partial class FacetRangeView
{
    private (decimal Min, decimal Max)? _bounds;
    private decimal _from;
    private decimal _to;
    private string? _known;

    private string Path => Group.ValuePath ?? Group.Field;

    private decimal Step
    {
        get
        {
            var (min, max) = _bounds!.Value;
            var whole = decimal.Truncate(min) == min && decimal.Truncate(max) == max && max - min >= 20;
            return whole ? 1 : Math.Max(0.01m, Math.Round((max - min) / 100, 2));
        }
    }

    private string Track
    {
        get
        {
            var (min, max) = _bounds!.Value;
            var span = max == min ? 1 : max - min;
            return FormattableString.Invariant($"--cw-from:{100 * (_from - min) / span:0.##}%;--cw-to:{100 * (_to - min) / span:0.##}%");
        }
    }

    protected override void OnParametersSet()
    {
        _bounds = Parse(Group.Min) is { } min && Parse(Group.Max) is { } max ? (min, max) : null;
        var chosen = Selection.Of(Group.Key).FirstOrDefault()?.OData;
        if (_bounds is { } bounds && chosen != _known)
        {
            _known = chosen;
            (_from, _to) = Bounds(chosen) ?? (bounds.Min, bounds.Max);
        }
    }

    private static decimal? Parse(string? text) => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Show(decimal value) => value.ToString(decimal.Truncate(value) == value ? "N0" : "N2", CultureInfo.CurrentCulture);

    private (decimal From, decimal To)? Bounds(string? odata) =>
        odata is not null && RangeText().Match(odata) is { Success: true } match ? (Parse(match.Groups[1].Value) ?? _from, Parse(match.Groups[2].Value) ?? _to) : null;

    private bool InRange(FacetOptionDto bucket) =>
        Selection.Of(Group.Key).Any() && Parse(bucket.Meta?.GetValueOrDefault("from")) is { } from && Parse(bucket.Meta?.GetValueOrDefault("to")) is { } to && from < _to && to > _from;

    private string BucketTitle(FacetOptionDto bucket) =>
        $"{Show(Parse(bucket.Meta?.GetValueOrDefault("from")) ?? 0)} – {Show(Parse(bucket.Meta?.GetValueOrDefault("to")) ?? 0)}: {Count(bucket.Count)}";

    private void Slide(ChangeEventArgs args, bool lower)
    {
        if (Parse(args.Value?.ToString()) is not { } value)
        {
            return;
        }

        if (lower)
        {
            _from = Math.Min(value, _to);
        }
        else
        {
            _to = Math.Max(value, _from);
        }
    }

    private Task TypeAsync(decimal value, bool lower)
    {
        Slide(new ChangeEventArgs { Value = Text(value) }, lower);
        return CommitAsync();
    }

    private Task PickAsync(FacetOptionDto bucket)
    {
        if (Bounds(bucket.OData.Replace(" lt ", " le ", StringComparison.Ordinal)) is { } range)
        {
            (_from, _to) = range;
        }

        return CommitAsync();
    }

    private Task CommitAsync()
    {
        var (min, max) = _bounds!.Value;
        if (_from <= min && _to >= max)
        {
            _known = null;
            Selection.Set(Group, null, null);
        }
        else
        {
            var odata = $"({Path} ge {Text(_from)} and {Path} le {Text(_to)})";
            _known = odata;
            Selection.Set(Group, $"{Show(_from)} – {Show(_to)}", odata);
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"ge (-?[0-9.]+) and \S+ l[te] (-?[0-9.]+)\)")]
    private static partial Regex RangeText();
}
