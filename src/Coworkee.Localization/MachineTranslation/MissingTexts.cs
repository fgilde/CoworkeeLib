using Coworkee.Contracts.Localization;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Languages;
using Coworkee.Localization.Resources;
using Coworkee.Localization.Texts;
using Coworkee.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>
/// Fills the texts a language lacks (module texts and keys clients reported) with machine translations. Texts are keyed by their English
/// wording, or start from their English text when the key is none; with another source language (setting <see cref="LocalizationSettings.TranslationSource"/>) a text starts from that language's wording when it has one.
/// </summary>
public sealed class MissingTexts(CoworkeeDbContext db, LocalizationResources resources, TextStore store, TextTranslator translator, ISettingProvider settings)
{
    private const string English = "en";

    public async Task<int> TranslateAsync(string culture, CancellationToken cancellationToken)
    {
        if (TranslationTexts.Language(culture) == English || !await translator.IsAvailableAsync(cancellationToken))
        {
            return 0;
        }

        var present = await store.GetAsync(culture, cancellationToken);
        var reported = await db.Set<TextKey>().AsNoTracking().Select(k => k.Key).ToListAsync(cancellationToken);
        var missing = resources.Keys.Concat(reported).Distinct(StringComparer.Ordinal).Where(key => !present.ContainsKey(key)).ToList();
        var source = await SourceAsync(culture, cancellationToken);
        var sourceTexts = source == English ? new Dictionary<string, string>() : await store.GetAsync(source, cancellationToken);
        var fromSource = missing.Where(sourceTexts.ContainsKey).ToList();
        var fromEnglish = missing.Except(fromSource).ToList();
        // a key that is no English text (e.g. imported "{Resource}::{key}") starts from its English text when it has one
        var english = await store.GetAsync(English, cancellationToken);
        string EnglishOf(string key) => english.TryGetValue(key, out var text) && text.Length > 0 ? text : key;

        var translatedSource = await translator.TranslateAsync([.. fromSource.Select(k => sourceTexts[k]).Distinct()], source, culture, cancellationToken);
        var translatedEnglish = await translator.TranslateAsync([.. fromEnglish.Select(EnglishOf).Distinct()], English, culture, cancellationToken);
        var translated = fromSource.Where(k => translatedSource.ContainsKey(sourceTexts[k])).Select(k => (Key: k, Value: translatedSource[sourceTexts[k]]))
            .Concat(fromEnglish.Where(k => translatedEnglish.ContainsKey(EnglishOf(k))).Select(k => (Key: k, Value: translatedEnglish[EnglishOf(k)])))
            .ToList();
        foreach (var (key, value) in translated)
        {
            db.Add(new Domain.Translation { Culture = culture, Key = key, Value = value });
        }

        await db.SaveChangesAsync(cancellationToken);
        return translated.Count;
    }

    // a language is not translated from itself; English is the fallback source
    private async Task<string> SourceAsync(string culture, CancellationToken cancellationToken) =>
        await settings.GetAsync(LocalizationSettings.TranslationSource, cancellationToken) is { Length: > 0 } source
        && InitialLanguages.IsCulture(source)
        && TranslationTexts.Language(source) != TranslationTexts.Language(culture)
            ? source.Trim()
            : English;
}
