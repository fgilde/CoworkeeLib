using System.Globalization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Languages;

internal sealed class GetLanguagesHandler(CoworkeeDbContext db, LocalizationResources resources) : IHandler<GetLanguagesQuery, Result<IReadOnlyList<LanguageDto>>>
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
        new[] { "en" }.Concat(resources.Cultures).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(culture => new LanguageDto(null, culture, NativeName(culture), true, culture == "en"))
            .OrderBy(l => l.Name, StringComparer.CurrentCulture);

    private static string NativeName(string culture)
    {
        var name = CultureInfo.GetCultureInfo(culture).NativeName;
        return char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
    }
}
