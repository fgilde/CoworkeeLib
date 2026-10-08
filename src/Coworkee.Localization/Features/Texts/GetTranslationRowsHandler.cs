using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Texts;

internal sealed class GetTranslationRowsHandler(CoworkeeDbContext db, LocalizationResources resources) : IHandler<GetTranslationRowsQuery, Result<IReadOnlyList<TranslationRowDto>>>
{
    public async Task<Result<IReadOnlyList<TranslationRowDto>>> HandleAsync(GetTranslationRowsQuery query, CancellationToken cancellationToken)
    {
        var defaults = resources.For(query.Culture);
        var edits = await db.Set<Translation>().AsNoTracking().Where(t => t.Culture == query.Culture).ToDictionaryAsync(t => t.Key, t => t.Value, cancellationToken);
        var seen = await db.Set<TextKey>().AsNoTracking().Select(k => k.Key).ToListAsync(cancellationToken);
        return resources.Keys.Concat(edits.Keys).Concat(seen).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(key => new TranslationRowDto(key, defaults.GetValueOrDefault(key), edits.GetValueOrDefault(key)))
            .ToList();
    }
}
