using System.Linq.Expressions;
using System.Text.Json;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>
/// A row of any entity set for CoworkeeDataTable, whose columns need properties: column i of the widget is property Ci.
/// ponytail: at most <see cref="MaxColumns"/> columns; a source generated row type per entity set would lift the limit.
/// </summary>
public sealed class DynamicRow
{
    public const int MaxColumns = 16;

    public object? C0 { get; set; }
    public object? C1 { get; set; }
    public object? C2 { get; set; }
    public object? C3 { get; set; }
    public object? C4 { get; set; }
    public object? C5 { get; set; }
    public object? C6 { get; set; }
    public object? C7 { get; set; }
    public object? C8 { get; set; }
    public object? C9 { get; set; }
    public object? C10 { get; set; }
    public object? C11 { get; set; }
    public object? C12 { get; set; }
    public object? C13 { get; set; }
    public object? C14 { get; set; }
    public object? C15 { get; set; }

    public static Expression<Func<DynamicRow, object?>> Column(int index)
    {
        var row = Expression.Parameter(typeof(DynamicRow), "r");
        return Expression.Lambda<Func<DynamicRow, object?>>(Expression.Property(row, $"C{index}"), row);
    }

    public static DynamicRow From(JsonElement element, IReadOnlyList<string> paths)
    {
        var row = new DynamicRow();
        for (var i = 0; i < Math.Min(paths.Count, MaxColumns); i++)
        {
            typeof(DynamicRow).GetProperty($"C{i}")!.SetValue(row, RowValues.Get(element, paths[i]));
        }

        return row;
    }
}
