using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<DataTransferKind>))]
public enum DataTransferKind
{
    Import,
    Export,
}
