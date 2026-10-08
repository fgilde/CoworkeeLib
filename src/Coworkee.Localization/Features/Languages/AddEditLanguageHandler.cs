using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Languages;

internal sealed class AddEditLanguageHandler(CoworkeeDbContext db, Coworkee.Localization.Texts.LocalizationChanges changes) : IHandler<AddEditLanguageCommand, Result<LanguageDto>>
{
    public async Task<Result<LanguageDto>> HandleAsync(AddEditLanguageCommand command, CancellationToken cancellationToken)
    {
        var request = command.Language;
        var culture = request.Culture.Trim();
        if (await db.Set<Language>().AnyAsync(l => l.Culture == culture && l.Id != command.Id, cancellationToken))
        {
            return Error.Conflict("localization.language_exists", "This language exists already.");
        }

        var language = command.Id is { } id
            ? await db.Set<Language>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken)
            : db.Add(new Language { Culture = culture, Name = request.Name }).Entity;
        if (language is null)
        {
            return Error.NotFound("localization.language_not_found", "The language does not exist.");
        }

        if (request.IsDefault)
        {
            await db.Set<Language>().Where(l => l.IsDefault && l.Id != language.Id).ForEachAsync(l => l.IsDefault = false, cancellationToken);
        }

        language.Culture = culture;
        language.Name = request.Name.Trim();
        language.IsEnabled = request.IsEnabled || request.IsDefault;
        language.IsDefault = request.IsDefault;
        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(cancellationToken);
        return new LanguageDto(language.Id, language.Culture, language.Name, language.IsEnabled, language.IsDefault);
    }
}
