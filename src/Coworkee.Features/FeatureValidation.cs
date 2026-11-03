using Coworkee.Core.Results;

namespace Coworkee.Features;

internal static class FeatureValidation
{
    public static Error? Validate(this IFeatureDefinitionManager definitions, IReadOnlyDictionary<string, string>? values)
    {
        foreach (var (name, value) in values ?? new Dictionary<string, string>())
        {
            if (definitions.Find(name) is not { } definition)
            {
                return Error.Validation(name, $"Feature '{name}' is not defined.");
            }

            if (definition.Validate(value) is { } message)
            {
                return Error.Validation(name, message);
            }
        }

        return null;
    }
}
