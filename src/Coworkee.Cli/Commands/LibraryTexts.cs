using System.Reflection;
using System.Text.Json;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>The texts of the library, embedded from Coworkee.Localization (UI) and Coworkee.AuthServer (account pages); English is the keys.</summary>
internal static class LibraryTexts
{
    private const string Ui = "Texts.Ui.";
    private const string Account = "Texts.Account.";

    public static IReadOnlyList<string> Languages { get; } = [.. OptionCatalog.ShippedLanguages.Order(StringComparer.Ordinal)];

    public static IReadOnlyList<KeyValuePair<string, string>> Load(string language, bool account)
    {
        var texts = Read(Ui, language);
        var keys = texts.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        return account ? [.. texts, .. Read(Account, language).Where(t => !keys.Contains(t.Key))] : texts;
    }

    // en has no file: the keys of the German one are the English texts
    private static List<KeyValuePair<string, string>> Read(string prefix, string language)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{prefix}{(language == "en" ? "de" : language)}.json")
            ?? throw new CliException($"No library texts for '{language}'. Shipped: {string.Join(", ", Languages)}.");
        using var document = JsonDocument.Parse(stream);
        return [.. document.RootElement.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, language == "en" ? p.Name : p.Value.GetString()!))];
    }
}
