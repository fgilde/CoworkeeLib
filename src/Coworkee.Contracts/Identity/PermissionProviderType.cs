using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Identity;

[JsonConverter(typeof(JsonStringEnumConverter<PermissionProviderType>))]
public enum PermissionProviderType
{
    Role,
    User,
    Group,
}
