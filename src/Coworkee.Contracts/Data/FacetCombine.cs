using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<FacetCombine>))]
public enum FacetCombine
{
    /// <summary>Rows with any of the chosen values (or).</summary>
    Any,

    /// <summary>Rows with all chosen values (and), for values that can hold together, such as flags.</summary>
    All,

    /// <summary>One value at a time.</summary>
    Single,
}
