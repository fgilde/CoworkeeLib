using System.Reflection;
using System.Text.Json;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>The texts of the library, embedded from Coworkee.Localization; English is the keys.</summary>
internal static class LibraryTexts
{
    private const string Ui = "Texts.Ui.";

    public static IReadOnlyList<string> Languages { get; } = [.. OptionCatalog.ShippedLanguages.Order(StringComparer.Ordinal)];

    // en has no file: the keys of the German one are the English texts
    public static IReadOnlyList<KeyValuePair<string, string>> Load(string language)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{Ui}{(language == "en" ? "de" : language)}.json")
            ?? throw new CliException($"No library texts for '{language}'. Shipped: {string.Join(", ", Languages)}.");
        using var document = JsonDocument.Parse(stream);
        return [.. document.RootElement.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, language == "en" ? p.Name : p.Value.GetString()!))];
    }
}
