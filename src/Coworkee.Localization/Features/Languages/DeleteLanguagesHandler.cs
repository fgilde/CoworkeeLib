using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Languages;

internal sealed class DeleteLanguagesHandler(CoworkeeDbContext db) : IHandler<DeleteLanguagesCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteLanguagesCommand command, CancellationToken cancellationToken)
    {
        var languages = await db.Set<Language>().Where(l => command.Ids.Contains(l.Id)).ToListAsync(cancellationToken);
        if (languages.Any(l => l.IsDefault))
        {
            return Error.Conflict("localization.default_language", "The default language cannot be deleted.");
        }

        db.RemoveRange(languages);
        return Result.Success();
    }
}
