using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Texts;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Texts;

internal sealed class SetTranslationHandler(CoworkeeDbContext db, LocalizationChanges changes) : IHandler<SetTranslationCommand, Result>
{
    public async Task<Result> HandleAsync(SetTranslationCommand command, CancellationToken cancellationToken)
    {
        var (culture, key, value) = command.Translation;
        var translation = await db.Set<Translation>().SingleOrDefaultAsync(t => t.Culture == culture && t.Key == key, cancellationToken);
        if (string.IsNullOrEmpty(value))
        {
            if (translation is not null)
            {
                db.Remove(translation);
            }
        }
        else if (translation is null)
        {
            db.Add(new Translation { Culture = culture, Key = key, Value = value });
        }
        else
        {
            translation.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(cancellationToken);
        return Result.Success();
    }
}
