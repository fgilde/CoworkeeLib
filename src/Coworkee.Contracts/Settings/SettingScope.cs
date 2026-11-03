using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SettingScope>))]
public enum SettingScope
{
    Global,
    Tenant,
    User,
}
