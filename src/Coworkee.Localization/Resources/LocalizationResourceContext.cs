using System.Reflection;
using System.Text.Json;

namespace Coworkee.Localization.Resources;

public sealed class LocalizationResourceContext
{
    private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, Dictionary<string, string>> Texts => _texts;

    public LocalizationResourceContext Add(string culture, IReadOnlyDictionary<string, string> texts)
    {
        if (!_texts.TryGetValue(culture, out var target))
        {
            _texts[culture] = target = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        foreach (var (key, value) in texts)
        {
            target[key] = value;
        }

        return this;
    }

    /// <summary>Adds every embedded resource named like <c>Localization/de.json</c> (a flat object of key and text).</summary>
    public LocalizationResourceContext AddEmbeddedJson(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Localization.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            var culture = Path.GetFileNameWithoutExtension(name)[(name.LastIndexOf(".Localization.", StringComparison.Ordinal) + ".Localization.".Length)..];
            using var stream = assembly.GetManifestResourceStream(name)!;
            Add(culture, JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? []);
        }

        return this;
    }
}
