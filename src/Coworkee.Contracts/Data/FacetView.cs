using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<FacetView>))]
public enum FacetView
{
    /// <summary>Follows the values: a toggle for yes/no, chips for enums, a range for numbers, presets for dates, a list otherwise.</summary>
    Auto,
    Checkbox,
    Chips,
    Select,
    Buttons,
    Range,
    Dates,
    Tree,
    Swatches,
    Toggle,
}
