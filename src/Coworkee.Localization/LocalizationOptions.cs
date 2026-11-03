namespace Coworkee.Localization;

/// <summary>The languages an installation starts with (section Coworkee:Localization); administrators change them later on the Languages page.</summary>
public sealed class LocalizationOptions
{
    public const string Section = "Coworkee:Localization";

    /// <summary>Cultures offered from the start, e.g. ["en", "fr"]; empty offers <see cref="Default"/>, the other shipped languages are for administrators to switch on.</summary>
    public List<string> Languages { get; set; } = [];

    public static readonly IReadOnlyList<string> Default = ["en", "de"];

    /// <summary>The language of users who chose none (default: English when offered, else the first language).</summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>The cultures to offer with the default among them.</summary>
    public IReadOnlyList<(string Culture, bool IsDefault)> Initial()
    {
        var cultures = (Languages.Count > 0 ? Languages : Default).Select(c => c.Trim()).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var fallback = cultures.Contains("en", StringComparer.OrdinalIgnoreCase) ? "en" : cultures.FirstOrDefault();
        var preferred = DefaultLanguage is { Length: > 0 } wanted && cultures.Contains(wanted, StringComparer.OrdinalIgnoreCase) ? wanted : fallback;
        return [.. cultures.Select(c => (c, string.Equals(c, preferred, StringComparison.OrdinalIgnoreCase)))];
    }
}
