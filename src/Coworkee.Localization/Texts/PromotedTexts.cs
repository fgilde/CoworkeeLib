using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Texts;

/// <summary>
/// Keys clients reported as missing (<see cref="TextKey"/>) that a resource ships by now: the machine translations stored for them while no
/// resource had them would keep winning over the shipped texts (stored texts override resources). Drops those nobody edited afterwards, for the
/// cultures the resources cover now; an administrator's edit of such a text stays, and so do the texts of cultures without shipped ones.
/// </summary>
public sealed class PromotedTexts(CoworkeeDbContext db, LocalizationResources resources, LocalizationChanges changes)
{
    /// <returns>The number of stored texts dropped in favour of the shipped ones.</returns>
    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        var shipped = resources.Keys.ToHashSet(StringComparer.Ordinal);
        var promoted = (await db.Set<TextKey>().ToListAsync(cancellationToken)).Where(k => shipped.Contains(k.Key)).ToList();
        if (promoted.Count == 0)
        {
            return 0;
        }

        var keys = promoted.Select(k => k.Key).ToList();
        var stale = (await db.Set<Translation>().Where(t => keys.Contains(t.Key) && t.ModifiedAt == null).ToListAsync(cancellationToken))
            .Where(t => resources.For(t.Culture).ContainsKey(t.Key))
            .ToList();
        db.RemoveRange(stale);
        db.RemoveRange(promoted);
        await db.SaveChangesAsync(cancellationToken);
        if (stale.Count > 0)
        {
            await changes.NotifyAsync(cancellationToken);
        }

        return stale.Count;
    }
}
