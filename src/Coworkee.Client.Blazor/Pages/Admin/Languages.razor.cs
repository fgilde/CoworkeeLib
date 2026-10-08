using System.Globalization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Languages
{
    private static readonly IReadOnlyList<LanguageGroup> AllGroups = BuildGroups();
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LanguageDto> _languages = [];
    private string? _filter;
    private bool _onlyEnabled;
    private string? _busy;

    [Inject] private ILocalizationApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private int EnabledCount => _languages.Count(l => l.IsEnabled);

    private IEnumerable<LanguageGroup> VisibleGroups => AllGroups
        .Where(g => !_onlyEnabled || EnabledIn(g) > 0)
        .Where(g => string.IsNullOrWhiteSpace(_filter) || g.Cultures.Any(Matches))
        .OrderByDescending(EnabledIn)
        .ThenBy(g => g.Neutral.EnglishName, StringComparer.CurrentCulture);

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync() => await Snackbar.RunAsync(async () => _languages = await Api.GetLanguagesAsync(includeDisabled: true));

    private LanguageDto? Find(CultureInfo culture) => _languages.FirstOrDefault(l => string.Equals(l.Culture, culture.Name, StringComparison.OrdinalIgnoreCase));

    private int EnabledIn(LanguageGroup group) => group.Cultures.Count(c => Find(c)?.IsEnabled == true);

    private bool IsOpen(LanguageGroup group) => _open.Contains(group.Neutral.Name) || !string.IsNullOrWhiteSpace(_filter);

    private void Toggle(LanguageGroup group, bool expanded)
    {
        if (expanded)
        {
            _open.Add(group.Neutral.Name);
        }
        else
        {
            _open.Remove(group.Neutral.Name);
        }
    }

    // "fr" finds French and fr-CH, not "Africa": codes and words are matched from their start
    private bool Matches(CultureInfo culture)
    {
        var filter = _filter!.Trim();
        return culture.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
            || new[] { culture.NativeName, culture.EnglishName, culture.DisplayName }
                .SelectMany(name => name.Split([' ', '(', ')', ',', '-'], StringSplitOptions.RemoveEmptyEntries))
                .Any(word => word.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase));
    }

    private IEnumerable<CultureInfo> CulturesOf(LanguageGroup group) =>
        string.IsNullOrWhiteSpace(_filter) || Matches(group.Neutral) ? group.Cultures : group.Cultures.Where(Matches);

    private static string Title(CultureInfo culture) => char.ToUpper(culture.NativeName[0], culture) + culture.NativeName[1..];

    private async Task SwitchAsync(CultureInfo culture, bool on)
    {
        await RunAsync(on ? L["Switching {0} on and translating its missing texts…", Title(culture)] : L["Switching {0} off…", Title(culture)], async () =>
        {
            var result = await Api.SetLanguageEnabledAsync(culture.Name, on);
            if (!on)
            {
                Snackbar.Add(L["{0} is switched off.", Title(culture)], Severity.Info);
            }
            else if (result.Translated > 0)
            {
                Snackbar.Add(L["{0} is switched on; {1} texts were translated.", Title(culture), result.Translated], Severity.Success);
            }
            else if (!result.TranslatorAvailable && !culture.Name.StartsWith(CoworkeeLocalizer.English, StringComparison.OrdinalIgnoreCase))
            {
                Snackbar.Add(L["{0} is switched on. Without a translator key, texts nobody translated show in English.", Title(culture)], Severity.Warning);
            }
            else
            {
                Snackbar.Add(L["{0} is switched on.", Title(culture)], Severity.Success);
            }
        });
    }

    private Task MakeDefaultAsync(LanguageDto language) =>
        RunAsync(L["Saving…"], () => Api.SaveLanguageAsync(language.Id, new AddEditLanguageRequest
        {
            Culture = language.Culture,
            Name = language.Name,
            IsEnabled = true,
            IsDefault = true,
        }));

    private Task TranslateAsync(CultureInfo culture) =>
        RunAsync(L["Translating the missing texts of {0}…", Title(culture)], async () =>
        {
            var result = await Api.TranslateMissingAsync(culture.Name);
            Snackbar.Add(
                result.TranslatorAvailable ? L["{0} texts were translated.", result.Translated] : L["Set a translator key in the settings (Localization) first."],
                result.TranslatorAvailable ? Severity.Success : Severity.Warning);
        });

    private async Task RunAsync(string message, Func<Task> action)
    {
        _busy = message;
        try
        {
            await Snackbar.RunAsync(action);
            await LoadAsync();
        }
        finally
        {
            _busy = null;
        }
    }

    private static IReadOnlyList<LanguageGroup> BuildGroups() =>
    [
        .. CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => c.Name.Length > 0)
            .Select(neutral => new LanguageGroup(neutral, [neutral, .. CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Where(c => c.Parent.Name == neutral.Name)
                .OrderBy(c => c.NativeName, StringComparer.CurrentCulture)])),
    ];

    private sealed record LanguageGroup(CultureInfo Neutral, IReadOnlyList<CultureInfo> Cultures);
}
