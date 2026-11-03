using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Mailing;

[JsonConverter(typeof(JsonStringEnumConverter<RecipientPreset>))]
public enum RecipientPreset
{
    AllActive,
    NotSignedInSince,
    NeverSignedIn,
    SignedUpWithin,
    EmailNotConfirmed,
    InRoles,
    Inactive,
}
