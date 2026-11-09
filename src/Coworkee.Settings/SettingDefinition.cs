using System.Globalization;
using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

public sealed record SettingDefinition(
    string Name,
    string Group,
    string DisplayName,
    SettingType Type,
    IReadOnlyList<SettingScope> Scopes,
    string? DefaultValue,
    bool VisibleToClient,
    string? Description,
    IReadOnlyList<string>? Choices,
    SettingCondition? EnabledWhen = null,
    string? Permission = null)
{
    public bool IsEncrypted => Type == SettingType.Secret;

    public bool IsClientVisible => VisibleToClient && !IsEncrypted;

    public string? Validate(string value) => Type switch
    {
        SettingType.Bool when !bool.TryParse(value, out _) => "Expected true or false.",
        SettingType.Int when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => "Expected a whole number.",
        SettingType.Choice when Choices?.Contains(value, StringComparer.Ordinal) != true => $"Expected one of: {string.Join(", ", Choices ?? [])}.",
        SettingType.MultiChoice when SplitChoices(value).Any(v => Choices?.Contains(v, StringComparer.Ordinal) != true) =>
            $"Expected some of: {string.Join(", ", Choices ?? [])}, comma separated.",
        _ => null,
    };

    /// <summary>The chosen values of a <see cref="SettingType.MultiChoice"/> value.</summary>
    public static IReadOnlyList<string> SplitChoices(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
