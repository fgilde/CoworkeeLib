using System.Globalization;
using System.Reflection;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    // ponytail: MudBlazor 9 keeps the grouping function internal; DataTableNullColumnsTests fail when it is renamed
    private static readonly FieldInfo GroupByFunction = typeof(Column<T>).GetField("groupBy", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // a property with [FacetDisplay] (or [CountryCode]) renders through its display's renderer, unless the page brings a cell template;
    // a value type behind a navigation that may be null (o => o.Customer.Rating) renders empty where MudBlazor would show its default
    private void FitCell(Column<T> column)
    {
        if (column.CellTemplate is not null || !DataGridLayout<T>.IsProperty(column) || column.PropertyName is not { } name)
        {
            return;
        }

        var path = new List<PropertyInfo>();
        var type = typeof(T);
        foreach (var part in name.Split('.'))
        {
            if (type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance) is not { } property)
            {
                return;
            }

            path.Add(property);
            type = property.PropertyType;
        }

        var render = path[^1].GetCustomAttribute<FacetDisplayAttribute>(inherit: true) is { } display ? Options.ValueRenderers.Find(display.Display) : null;
        var defaultBehindNull = type.IsValueType && Nullable.GetUnderlyingType(type) is null
            && path.SkipLast(1).Any(p => !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) is not null);
        if (render is null && !defaultBehindNull)
        {
            return;
        }

        var format = column.GetType().GetProperty(nameof(PropertyColumn<T, object>.Format))?.GetValue(column) as string;
#pragma warning disable BL0005 // only where the page left the parameter unset
        column.CellTemplate = cell =>
        {
            var value = path.Aggregate((object?)cell.Item, (item, property) => item is null ? null : property.GetValue(item));
            if (render is null)
            {
                return builder => builder.AddContent(0, value is IFormattable formattable ? formattable.ToString(format, column.Culture ?? CultureInfo.CurrentCulture) : value);
            }

            var text = Convert.ToString(value, CultureInfo.CurrentCulture);
            return render(new FacetValueContext(text, text ?? string.Empty));
        };
#pragma warning restore BL0005
    }

    // MudBlazor compiles the cell with null checks that test each step through the unguarded steps before it (MudBlazor 9.12 ExpressionNull), so
    // o => o.Supplier.Organization.Name throws in every cell where Supplier is null, even with a cell template (Cell's constructor reads the value
    // for editing); it groups the loaded rows (and sorts its filtered items) through the plain expression, so o => o.Status.Code throws there.
    // The table gives such columns a null safe getter for cell, sort and group unless the page set its own SortBy or GroupBy.
    // DataTableNullSafeColumns calls this after every parameter set of the columns, as the column compiles its cell again for each new expression.
    internal static void FitNullSafe(Column<T> column)
    {
        if (column is IPropertyLikeColumn || !DataGridLayout<T>.IsProperty(column) || column.PropertyName is not { } name
            || !name.Contains('.') || PropertyPath.TypeOf(typeof(T), name) is null)
        {
            return;
        }

        var get = PropertyPath.Getter<T>(name);
#pragma warning disable BL0005 // only where the page left the parameter unset: MudBlazor's own function is a method of the column
        if (column.SortBy.Target == column)
        {
            column.SortBy = get;
        }
#pragma warning restore BL0005

        if (GroupByFunction.GetValue(column) is not Delegate group || group.Target == column)
        {
            GroupByFunction.SetValue(column, get);
        }

        // ponytail: MudBlazor 9 keeps the compiled cell private; DataTableNullColumnsTests fail when it is renamed
        if (column.GetType().GetField("_cellContentFunc", BindingFlags.Instance | BindingFlags.NonPublic) is { } cell && !ReferenceEquals(cell.GetValue(column), get))
        {
            cell.SetValue(column, get);
        }
    }
}
