using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Mailing;

[JsonConverter(typeof(JsonStringEnumConverter<RecipientSourceKind>))]
public enum RecipientSourceKind
{
    Preset,
    Query,
    Manual,

    /// <summary>A <see cref="Identity.UserSelection"/>: filter of the Users set, users added and left out.</summary>
    Selection,
}
