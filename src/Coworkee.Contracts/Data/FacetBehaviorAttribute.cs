using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

/// <summary>
/// How a facet ([ProvideFacet]) looks and combines its values. Unset values follow the facet: <c>MultiSelect = false</c> means
/// <see cref="FacetCombine.Single"/>, <c>GroupOperator = And</c> means <see cref="FacetCombine.All"/>; the view follows the value type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FacetBehaviorAttribute : Attribute
{
    private FacetCombine? _combine;

    public FacetView View { get; set; }

    /// <summary>How chosen values combine: any of them (or), all of them (and) or only one.</summary>
    public FacetCombine Combine
    {
        get => _combine ?? FacetCombine.Any;
        set => _combine = value;
    }

    /// <summary><see cref="Combine"/> when it was set.</summary>
    public FacetCombine? ConfiguredCombine => _combine;

    /// <summary>Values can be excluded ("not Cancelled") as well as chosen.</summary>
    public bool AllowExclude { get; set; }

    /// <summary>Users may switch between "any of" and "all of" and between the views that fit the values.</summary>
    public bool Switchable { get; set; }

    /// <summary>Splits values into a tree for <see cref="FacetView.Tree"/>, e.g. "Europe / Germany / Berlin".</summary>
    public string? TreeSeparator { get; set; }

    /// <summary>Bars of the histogram of number ranges.</summary>
    public int Buckets { get; set; } = 12;
}

/// <summary>The color of an enum member in facet swatches, e.g. <c>[FacetColor("#2e7d32")]</c> or a CSS color name.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class FacetColorAttribute(string color) : Attribute
{
    public string Color { get; } = color;
}

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

[JsonConverter(typeof(JsonStringEnumConverter<FacetValueKind>))]
public enum FacetValueKind
{
    Text,
    Number,
    Date,
    Boolean,
    Enum,
    Flags,
    Reference,
}
