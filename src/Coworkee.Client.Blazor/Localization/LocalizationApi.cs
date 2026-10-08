using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;

namespace Coworkee.Client.Blazor.Localization;

internal sealed class LocalizationApi(HttpClient http) : ApiClientBase(http), ILocalizationApi
{
    private const string Root = "api/v1/localization";

    public async Task<IReadOnlyList<LanguageDto>> GetLanguagesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) =>
        await GetAsync<LanguageDto[]>(includeDisabled ? $"{Root}/languages/all" : $"{Root}/languages", cancellationToken);

    public Task<TextsDto> GetTextsAsync(string culture, CancellationToken cancellationToken = default) =>
        GetAsync<TextsDto>($"{Root}/texts/{Uri.EscapeDataString(culture)}", cancellationToken);

    public Task ReportMissingAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/missing", new MissingTextsRequest(keys), cancellationToken);

    public Task<LanguageDto> SaveLanguageAsync(Guid? id, AddEditLanguageRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing
            ? SendAsync<LanguageDto>(HttpMethod.Put, $"{Root}/languages/{existing}", request, cancellationToken)
            : SendAsync<LanguageDto>(HttpMethod.Post, $"{Root}/languages", request, cancellationToken);

    public Task DeleteLanguagesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/languages/delete", new IdListRequest(ids), cancellationToken);

    public async Task<IReadOnlyList<TranslationRowDto>> GetTranslationRowsAsync(string culture, CancellationToken cancellationToken = default) =>
        await GetAsync<TranslationRowDto[]>($"{Root}/translations/{Uri.EscapeDataString(culture)}", cancellationToken);

    public Task SetTranslationAsync(SetTranslationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/translations", request, cancellationToken);
}
