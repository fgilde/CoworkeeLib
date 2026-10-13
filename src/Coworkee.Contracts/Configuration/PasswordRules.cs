using System.Globalization;

namespace Coworkee.Contracts.Configuration;

/// <summary>The password rules in force, as the clients show them next to a password field.</summary>
public sealed record PasswordRules(
    int RequiredLength, int RequiredUniqueChars, bool RequireDigit, bool RequireLowercase, bool RequireUppercase, bool RequireNonAlphanumeric)
{
    public const int MaxLength = 128;

    /// <summary>The rules of the (client visible) settings; a missing or unreadable value keeps the identity default.</summary>
    public static PasswordRules From(IReadOnlyDictionary<string, string?> values) => new PasswordRules(
        Number(values, PasswordSettings.RequiredLength, 8),
        Number(values, PasswordSettings.RequiredUniqueChars, 1),
        Flag(values, PasswordSettings.RequireDigit),
        Flag(values, PasswordSettings.RequireLowercase),
        Flag(values, PasswordSettings.RequireUppercase),
        Flag(values, PasswordSettings.RequireNonAlphanumeric)).Normalized();

    /// <summary>Rules a password can meet: a length of 1 to <see cref="MaxLength"/>, no more different characters than that length. Server and clients both resolve through here.</summary>
    public PasswordRules Normalized()
    {
        var length = Math.Clamp(RequiredLength, 1, MaxLength);
        return this with { RequiredLength = length, RequiredUniqueChars = Math.Min(RequiredUniqueChars, length) };
    }

    /// <summary>"At least 12 characters, a digit, an uppercase letter." with every part passed through <paramref name="translate"/> (English text and arguments).</summary>
    public string Describe(Func<string, object?[], string> translate)
    {
        var parts = new List<string> { translate("At least {0} characters", [RequiredLength]) };
        if (RequiredUniqueChars > 1)
        {
            parts.Add(translate("{0} different characters", [RequiredUniqueChars]));
        }

        AddIf(RequireDigit, "a digit");
        AddIf(RequireLowercase, "a lowercase letter");
        AddIf(RequireUppercase, "an uppercase letter");
        AddIf(RequireNonAlphanumeric, "a special character");
        return string.Join(", ", parts) + ".";

        void AddIf(bool required, string text)
        {
            if (required)
            {
                parts.Add(translate(text, []));
            }
        }
    }

    private static int Number(IReadOnlyDictionary<string, string?> values, string name, int fallback) =>
        int.TryParse(values.GetValueOrDefault(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    private static bool Flag(IReadOnlyDictionary<string, string?> values, string name) =>
        !bool.TryParse(values.GetValueOrDefault(name), out var on) || on;
}
