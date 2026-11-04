using System.Globalization;
using System.Reflection;
using Coworkee.Contracts.Data;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    // a property with [FacetDisplay] (or [CountryCode]) renders through its display's renderer, unless the page brings a cell template
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

        if (path[^1].GetCustomAttribute<FacetDisplayAttribute>(inherit: true) is not { } display || Options.ValueRenderers.Find(display.Display) is not { } render)
        {
            return;
        }

#pragma warning disable BL0005 // only where the page left the parameter unset
        column.CellTemplate = cell =>
        {
            var value = path.Aggregate((object?)cell.Item, (item, property) => item is null ? null : property.GetValue(item));
            var text = Convert.ToString(value, CultureInfo.CurrentCulture);
            return render(new FacetValueContext(text, text ?? string.Empty));
        };
#pragma warning restore BL0005
    }
}
