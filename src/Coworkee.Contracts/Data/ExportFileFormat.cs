using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<ExportFileFormat>))]
public enum ExportFileFormat
{
    Xlsx,
    Csv,
    Json,
}
