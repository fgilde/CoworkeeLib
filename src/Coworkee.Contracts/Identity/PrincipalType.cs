using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Identity;

[JsonConverter(typeof(JsonStringEnumConverter<PrincipalType>))]
public enum PrincipalType
{
    User,
    Group,
}
