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

    /// <summary>Any number of <c>Choices</c>, stored comma separated; an empty value (not null) is "none chosen".</summary>
    MultiChoice,
}
