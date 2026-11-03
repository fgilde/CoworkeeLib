using System.Globalization;
using Coworkee.Contracts.Features;

namespace Coworkee.Features;

public sealed record FeatureDefinition(string Name, string Group, string DisplayName, FeatureType Type, string? DefaultValue, string? Description)
{
    public string? Validate(string value) => Type switch
    {
        FeatureType.Bool when !bool.TryParse(value, out _) => "Expected true or false.",
        FeatureType.Int when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => "Expected a whole number.",
        _ => null,
    };
}
