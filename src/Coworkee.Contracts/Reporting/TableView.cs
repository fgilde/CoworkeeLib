using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<TableView>))]
public enum TableView
{
    Table,
    Cards,
    List,
}
