using System.Collections.Concurrent;
using System.Globalization;
using Coworkee.Application.Localization;

namespace Coworkee.Localization.Resources;

/// <summary>The module texts (<c>Localization/{culture}.json</c>) for server-side messages; edits in the translations page are not part of them.</summary>
internal sealed class ResourceTextTranslator(LocalizationResources resources) : ITextTranslator
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cultures = new(StringComparer.OrdinalIgnoreCase);

    public string? Translate(string text, CultureInfo culture) =>
        _cultures.GetOrAdd(culture.Name, resources.For).TryGetValue(text, out var translated) ? translated : null;
}
