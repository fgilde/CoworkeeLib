using Coworkee.Contracts.Localization;

namespace Coworkee.Mailing;

public static class MailCultures
{
    public const string Fallback = "en";

    /// <summary>Sort key that puts the fallback culture first, so the fallback among several cultures is the same everywhere.</summary>
    public static int FallbackOrder(string culture) => string.Equals(culture, Fallback, StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    /// <summary>The cultures to look for, in order: the culture, its parent, the default language and its parent, then English.</summary>
    public static IReadOnlyList<string> Candidates(string? culture, string? defaultCulture = null)
    {
        var candidates = new List<string>();
        foreach (var wanted in new[] { culture, defaultCulture }.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            candidates.AddRange(ShippedCultures.Files(wanted!));
        }

        candidates.Add(Fallback);
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
