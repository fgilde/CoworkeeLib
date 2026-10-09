using System.Text.RegularExpressions;

namespace Coworkee.AuthServer.Registration;

/// <summary>Patterns with * as wildcard, compared without case; an empty list allows everything.</summary>
public static class Wildcards
{
    public static bool Allows(IReadOnlyCollection<string> patterns, string? value) =>
        patterns.Count == 0 || (value is not null && patterns.Any(p => Regex.IsMatch(value, "^" + Regex.Escape(p.Trim()).Replace(@"\*", ".*", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))));
}
