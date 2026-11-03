using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetType>))]
public enum WidgetType
{
    Kpi,
    Chart,
    Table,
    DataTable,
    Pivot,
    Markdown,
    Image,
    Embed,
}
