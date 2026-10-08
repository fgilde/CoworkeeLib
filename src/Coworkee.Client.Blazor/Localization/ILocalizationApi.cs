using Coworkee.Contracts.Localization;

namespace Coworkee.Client.Blazor.Localization;

public interface ILocalizationApi
{
    Task<IReadOnlyList<LanguageDto>> GetLanguagesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    Task<TextsDto> GetTextsAsync(string culture, CancellationToken cancellationToken = default);

    Task ReportMissingAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    Task<LanguageDto> SaveLanguageAsync(Guid? id, AddEditLanguageRequest request, CancellationToken cancellationToken = default);

    Task DeleteLanguagesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranslationRowDto>> GetTranslationRowsAsync(string culture, CancellationToken cancellationToken = default);

    Task SetTranslationAsync(SetTranslationRequest request, CancellationToken cancellationToken = default);
}
