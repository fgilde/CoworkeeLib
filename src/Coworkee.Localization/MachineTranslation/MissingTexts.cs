using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Coworkee.Localization.Texts;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>Fills the texts a language lacks (module texts and keys clients reported) with machine translations.</summary>
public sealed class MissingTexts(CoworkeeDbContext db, LocalizationResources resources, TextStore store, TextTranslator translator)
{
    public async Task<int> TranslateAsync(string culture, CancellationToken cancellationToken)
    {
        if (culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) || !await translator.IsAvailableAsync(cancellationToken))
        {
            return 0;
        }

        var present = await store.GetAsync(culture, cancellationToken);
        var reported = await db.Set<TextKey>().AsNoTracking().Select(k => k.Key).ToListAsync(cancellationToken);
        var missing = resources.Keys.Concat(reported).Distinct(StringComparer.Ordinal).Where(key => !present.ContainsKey(key)).ToList();
        var translated = await translator.TranslateAsync(missing, culture, cancellationToken);
        foreach (var (key, value) in translated)
        {
            db.Add(new Domain.Translation { Culture = culture, Key = key, Value = value });
        }

        await db.SaveChangesAsync(cancellationToken);
        return translated.Count;
    }
}
