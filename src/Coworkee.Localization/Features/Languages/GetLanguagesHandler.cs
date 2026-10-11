using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Languages;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Localization.Features.Languages;

internal sealed class GetLanguagesHandler(CoworkeeDbContext db, LocalizationResources resources, IOptions<LocalizationOptions> options) : IHandler<GetLanguagesQuery, Result<IReadOnlyList<LanguageDto>>>
{
    public async Task<Result<IReadOnlyList<LanguageDto>>> HandleAsync(GetLanguagesQuery query, CancellationToken cancellationToken)
    {
        var stored = await db.Set<Language>().AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
        if (stored.Count == 0)
        {
            return ShippedLanguages().ToList();
        }

        return stored.Where(l => query.IncludeDisabled || l.IsEnabled)
            .Select(l => new LanguageDto(l.Id, l.Culture, l.Name, l.IsEnabled, l.IsDefault))
            .ToList();
    }

    private IEnumerable<LanguageDto> ShippedLanguages() =>
        InitialLanguages.Create(options.Value, resources)
            .Select(l => new LanguageDto(null, l.Culture, l.Name, true, l.IsDefault))
            .OrderBy(l => l.Name, StringComparer.CurrentCulture);
}
