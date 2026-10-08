using System.Globalization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Domain;
using Coworkee.Localization.Resources;
using Coworkee.Localization.Texts;
using Coworkee.Localization.MachineTranslation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Localization.Features.Languages;

internal sealed class SetLanguageEnabledHandler(
    CoworkeeDbContext db, LocalizationResources resources, MissingTexts missing, TextTranslator translator, LocalizationChanges changes)
    : IHandler<SetLanguageEnabledCommand, Result<LanguageSwitchDto>>,
      IHandler<TranslateMissingCommand, Result<TranslateMissingDto>>
{
    public async Task<Result<LanguageSwitchDto>> HandleAsync(SetLanguageEnabledCommand command, CancellationToken cancellationToken)
    {
        if (Culture(command.Culture) is not { } culture)
        {
            return Error.Validation(nameof(command.Culture), "Unknown culture.");
        }

        await EnsureStoredAsync(cancellationToken);
        var language = await db.Set<Language>().SingleOrDefaultAsync(l => l.Culture == culture.Name, cancellationToken)
            ?? db.Add(new Language { Culture = culture.Name, Name = NativeName(culture) }).Entity;
        if (!command.Enabled && language.IsDefault)
        {
            return Error.Conflict("localization.default_language", "The default language cannot be switched off. Make another language the default first.");
        }

        language.IsEnabled = command.Enabled;
        await db.SaveChangesAsync(cancellationToken);
        var translated = command.Enabled ? await missing.TranslateAsync(culture.Name, cancellationToken) : 0;
        await changes.NotifyAsync(cancellationToken);
        return new LanguageSwitchDto(
            new LanguageDto(language.Id, language.Culture, language.Name, language.IsEnabled, language.IsDefault), translated, await translator.IsAvailableAsync(cancellationToken));
    }

    public async Task<Result<TranslateMissingDto>> HandleAsync(TranslateMissingCommand command, CancellationToken cancellationToken)
    {
        if (Culture(command.Culture) is not { } culture)
        {
            return Error.Validation(nameof(command.Culture), "Unknown culture.");
        }

        var translated = await missing.TranslateAsync(culture.Name, cancellationToken);
        if (translated > 0)
        {
            await changes.NotifyAsync(cancellationToken);
        }

        return new TranslateMissingDto(translated, await translator.IsAvailableAsync(cancellationToken));
    }

    // until the first switch, the shipped languages are offered without rows; keep them when the list becomes explicit
    private async Task EnsureStoredAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<Language>().AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var shipped in new[] { "en" }.Concat(resources.Cultures).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            db.Add(new Language { Culture = shipped, Name = NativeName(CultureInfo.GetCultureInfo(shipped)), IsDefault = shipped == "en" });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static CultureInfo? Culture(string name)
    {
        try
        {
            return string.IsNullOrWhiteSpace(name) ? null : CultureInfo.GetCultureInfo(name.Trim(), predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static string NativeName(CultureInfo culture) => char.ToUpper(culture.NativeName[0], culture) + culture.NativeName[1..];
}
