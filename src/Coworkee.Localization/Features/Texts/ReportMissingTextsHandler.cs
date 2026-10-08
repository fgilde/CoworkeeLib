using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Texts;

internal sealed class ReportMissingTextsHandler(CoworkeeDbContext db, LocalizationResources resources, TimeProvider clock) : IHandler<ReportMissingTextsCommand, Result>
{
    public async Task<Result> HandleAsync(ReportMissingTextsCommand command, CancellationToken cancellationToken)
    {
        var shipped = resources.Keys.ToHashSet(StringComparer.Ordinal);
        var candidates = command.Keys.Distinct(StringComparer.Ordinal).Where(k => !shipped.Contains(k)).ToList();
        var known = await db.Set<TextKey>().Where(k => candidates.Contains(k.Key)).Select(k => k.Key).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        db.AddRange(candidates.Except(known, StringComparer.Ordinal).Select(key => new TextKey { Key = key, FirstSeenAt = now }));
        return Result.Success();
    }
}
