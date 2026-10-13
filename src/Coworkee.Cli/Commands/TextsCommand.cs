using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coworkee.Cli.Commands;

internal static class TextsCommand
{
    private static readonly Option<string> Lang = new("--lang") { Description = "Language code of the library texts, for example fr (en exports the keys as texts).", Required = true };
    private static readonly Option<string?> Keys = new("--keys") { Description = "Only the keys that match: a glob with * and ?, or a regular expression between slashes, for example \"Filter*\" or \"/^Sign/\"." };
    private static readonly Option<string?> Out = new("--out") { Description = "Target file (default: src/<App>.Contracts/Localization/<lang>.json of the solution here)." };

    public static Command Create()
    {
        var export = new Command("export", "Copy the library texts of a language into the app's translation file so you can change them. Existing keys keep their text, new keys are added at the end.")
        {
            Lang, Keys, Out,
        }.Handle((result, _) =>
        {
            var language = result.GetValue(Lang)!;
            var output = result.GetValue(Out) ?? DefaultOutput(Solution.Find(Environment.CurrentDirectory), language);
            var count = Export(output, language, result.GetValue(Keys));
            Console.WriteLine($"{count} texts written to {output}.");
            return Task.FromResult(0);
        });
        return new Command("texts", "Work with the texts of the library.") { export };
    }

    public static string DefaultOutput(Solution solution, string language) =>
        Path.Combine(solution.Src, $"{solution.Name}.Contracts", "Localization", language + ".json");

    public static int Export(string output, string language, string? keys)
    {
        var pattern = keys is null ? null : Pattern(keys);
        var texts = LibraryTexts.Load(language).Where(t => pattern?.IsMatch(t.Key) != false).ToList();
        var merged = Existing(output);
        var present = merged.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        merged.AddRange(texts.Where(t => !present.Contains(t.Key)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, Write(merged));
        return texts.Count;
    }

    public static bool Matches(string pattern, string key) => Pattern(pattern).IsMatch(key);

    private static Regex Pattern(string pattern)
    {
        try
        {
            return pattern.Length > 2 && pattern[0] == '/' && pattern[^1] == '/'
                ? new Regex(pattern[1..^1], RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                : new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }
        catch (ArgumentException exception)
        {
            throw new CliException($"--keys '{pattern}' is not a valid pattern: {exception.Message}");
        }
    }

    private static List<KeyValuePair<string, string>> Existing(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return [.. document.RootElement.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString() ?? string.Empty))];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new CliException($"{path} is not a flat JSON object of texts ({exception.Message}); nothing was changed.");
        }
    }

    private static string Write(IEnumerable<KeyValuePair<string, string>> texts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\r\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in texts)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\r\n";
    }
}
