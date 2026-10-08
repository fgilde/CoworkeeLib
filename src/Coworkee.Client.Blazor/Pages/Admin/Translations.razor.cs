using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Translations
{
    private IReadOnlyList<LanguageDto> _languages = [];
    private List<TranslationRowDto> _rows = [];
    private string? _culture;
    private string? _search;
    private bool _onlyMissing;

    [Inject] private ILocalizationApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IEnumerable<TranslationRowDto> Visible => _rows
        .Where(r => !_onlyMissing || Text(r) is null)
        .Where(r => string.IsNullOrWhiteSpace(_search) || r.Key.Contains(_search, StringComparison.CurrentCultureIgnoreCase)
            || Text(r)?.Contains(_search, StringComparison.CurrentCultureIgnoreCase) == true);

    protected override async Task OnInitializedAsync()
    {
        await Snackbar.RunAsync(async () => _languages = await Api.GetLanguagesAsync(includeDisabled: true));
        var first = _languages.FirstOrDefault(l => l.Culture == L.Culture && l.Culture != CoworkeeLocalizer.English)
            ?? _languages.FirstOrDefault(l => l.Culture != CoworkeeLocalizer.English);
        if (first is not null)
        {
            await SelectAsync(first.Culture);
        }
    }

    private static string? Text(TranslationRowDto row) => row.Value ?? row.Default;

    private async Task SelectAsync(string culture)
    {
        _culture = culture;
        await Snackbar.RunAsync(async () => _rows = [.. await Api.GetTranslationRowsAsync(culture)]);
    }

    private async Task SaveAsync(TranslationRowDto row, string? value)
    {
        var edit = string.IsNullOrWhiteSpace(value) || value == row.Default ? null : value;
        if (await Snackbar.RunAsync(() => Api.SetTranslationAsync(new SetTranslationRequest(_culture!, row.Key, edit))))
        {
            _rows[_rows.IndexOf(row)] = row with { Value = edit };
        }
    }
}
