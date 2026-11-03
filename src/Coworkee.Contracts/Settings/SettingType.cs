using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SettingType>))]
public enum SettingType
{
    String,
    Text,
    Bool,
    Int,
    Secret,
    Choice,
    Language,
}
