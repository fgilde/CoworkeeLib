using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Theming;

[JsonConverter(typeof(JsonStringEnumConverter<SignInMediaSide>))]
public enum SignInMediaSide
{
    Left,
    Right,
}
