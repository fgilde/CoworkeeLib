using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class LanguagesStep : StepViewModel
{
    private static readonly Dictionary<string, (string[] Pros, string[] Cons)> Facts = new()
    {
        ["libreTranslate"] = (["Free, no account", "Texts stay on your machine", "Works offline after the download"], ["About 600 MB container plus about 100 MB per language", "Good, not the best quality"]),
        ["myMemory"] = (["Free, no account", "Nothing to install"], ["5,000 characters a day, 50,000 with an email", "Texts go to a public service"]),
        ["deepL"] = (["Best quality for European languages", "500,000 characters a month free"], ["Account and key needed", "Texts go to DeepL"]),
        ["azure"] = (["2 million characters a month free", "Over 100 languages"], ["Azure account, key and region needed", "Texts go to Microsoft"]),
        ["google"] = (["500,000 characters a month free", "Over 100 languages"], ["Google Cloud account and key needed", "Texts go to Google"]),
        ["ai"] = (["Natural wording that follows software terms", "Uses the model of the AI assistant"], ["Costs per token of the model", "Needs the AI assistant module"]),
        ["none"] = (["Nothing leaves the app"], ["Texts without a translation show in English"]),
    };

    private string? _search;

    public LanguagesStep(MainViewModel main)
        : base(main)
    {
        LanguagesOption = main.Option("languages");
        DefaultOption = main.Option("defaultLanguage");
        ProviderOption = main.Option("translationProvider");
        SourceOption = main.Option("translationSource");
        Languages = [.. OptionCatalog.Languages.Select(c => new LanguageItemViewModel(LanguagesOption, c))];
        Services = [.. ProviderOption.Choices.Select(c => new TranslationServiceViewModel(ProviderOption, c, Facts[c.Value].Pros, Facts[c.Value].Cons))];
    }

    protected override string TitleText => "Languages";

    protected override string SubtitleText => "Which languages the app speaks and who translates them.";

    protected override string PitchText => "English and German ship with Coworkee. Every other language is machine translated at the first start, and administrators review the texts on the Translations page.";

    public override string Icon => "Icon.Translate";

    public OptionViewModel LanguagesOption { get; }

    public OptionViewModel DefaultOption { get; }

    public OptionViewModel ProviderOption { get; }

    public OptionViewModel SourceOption { get; }

    public IReadOnlyList<LanguageItemViewModel> Languages { get; }

    public IReadOnlyList<TranslationServiceViewModel> Services { get; }

    public string? Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
            {
                OnPropertyChanged(nameof(VisibleLanguages));
            }
        }
    }

    public IReadOnlyList<LanguageItemViewModel> VisibleLanguages => [.. Languages.Where(l => l.Matches(Search))];

    /// <summary>The checked languages; while none is checked the app offers the shipped ones.</summary>
    public IReadOnlyList<LanguageItemViewModel> Chosen => [.. Languages.Where(l => l.IsChecked)];

    public LanguageItemViewModel? Default
    {
        get => Languages.FirstOrDefault(l => l.Code == DefaultOption.Value);
        set
        {
            if (value is not null)
            {
                DefaultOption.Value = value.Code;
            }
        }
    }

    public string ChosenText => L.Format("{0} languages, {1} machine translated", Chosen.Count, Chosen.Count(l => !l.IsShipped));

    public string? ServiceNote => ProviderOption.Value switch
    {
        "libreTranslate" => "The app host starts LibreTranslate with the models of the chosen languages; the first start waits for their download.",
        "deepL" => "Key in the user secrets of the app host (Parameters:translation-key); keys ending in :fx use the free plan.",
        "azure" => "Key and region in the user secrets of the app host: Parameters:translation-key, translation-region.",
        "google" => "Key in the user secrets of the app host (Parameters:translation-key).",
        "myMemory" => "Optionally an email in the user secrets of the app host (Parameters:translation-email) for 50,000 characters a day.",
        "ai" => "Uses provider, model and key of the AI assistant.",
        _ => "Languages without shipped texts show English until someone translates them on the Translations page.",
    };

    public string ServiceNoteText => L.Get(ServiceNote!);

    /// <summary>The options this page shows instead of the architecture page.</summary>
    public static IReadOnlyList<string> Ids => [.. OptionCatalog.LanguageOptions, "translationProvider"];

    [RelayCommand]
    public void SelectService(TranslationServiceViewModel service) => service.Select();

    public override bool Owns(string error) => Ids.Any(id => error.StartsWith(id + ":", StringComparison.Ordinal));

    /// <summary>The flag of a language's main country as regional indicator letters, e.g. FR for fr.</summary>
    public static string Flag(string code)
    {
        var region = code switch
        {
            "en" => "GB", "pt-BR" => "BR", "zh-Hans" => "CN", "zh-Hant" => "TW", "ar" => "SA", "he" => "IL", "hi" => "IN", "ja" => "JP", "ko" => "KR",
            "sv" => "SE", "da" => "DK", "nb" => "NO", "et" => "EE", "el" => "GR", "uk" => "UA", "cs" => "CZ", "sl" => "SI", "vi" => "VN",
            _ => code[..2].ToUpperInvariant(),
        };
        return string.Concat(region.Select(c => char.ConvertFromUtf32(0x1F1E6 + c - 'A')));
    }

    public override void Refresh()
    {
        foreach (var language in Languages)
        {
            language.Refresh();
        }

        foreach (var service in Services)
        {
            service.Refresh();
        }

        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(Default));
        OnPropertyChanged(nameof(ChosenText));
        OnPropertyChanged(nameof(ServiceNoteText));
    }
}
