namespace Coworkee.Localization.Resources;

/// <summary>The texts of all module resources, built once.</summary>
public sealed class LocalizationResources
{
    private readonly IReadOnlyDictionary<string, Dictionary<string, string>> _texts;

    public LocalizationResources(IEnumerable<ILocalizationResourceContributor> contributors)
    {
        var context = new LocalizationResourceContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        _texts = context.Texts;
    }

    public IReadOnlyCollection<string> Cultures => [.. _texts.Keys];

    public IEnumerable<string> Keys => _texts.Values.SelectMany(t => t.Keys).Distinct(StringComparer.Ordinal);

    /// <summary>Texts of the culture and its parent ("de-AT" falls back to "de").</summary>
    public IReadOnlyDictionary<string, string> For(string culture)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in CultureChain(culture).Reverse())
        {
            if (_texts.TryGetValue(name, out var texts))
            {
                foreach (var (key, value) in texts)
                {
                    result[key] = value;
                }
            }
        }

        return result;
    }

    internal static IEnumerable<string> CultureChain(string culture)
    {
        yield return culture;
        if (culture.IndexOf('-') is var dash and > 0)
        {
            yield return culture[..dash];
        }
    }
}
