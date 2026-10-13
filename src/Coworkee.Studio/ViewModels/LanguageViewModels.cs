using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>A language to offer: its own name, the English one, a flag and whether the library ships its texts.</summary>
public sealed partial class LanguageItemViewModel(OptionViewModel languages, OptionChoice choice) : ObservableObject
{
    public string Code => choice.Value;

    public string NativeName => choice.Description;

    public string EnglishName => CultureInfo.GetCultureInfo(Code).EnglishName;

    public string Flag => LanguagesStep.Flag(Code);

    public bool IsShipped => OptionCatalog.ShippedLanguages.Contains(Code);

    public string Caption => IsShipped ? $"{EnglishName} · {L.Get("shipped")}" : EnglishName;

    /// <summary>Checked when chosen; while nothing is chosen the default languages are.</summary>
    public bool IsChecked
    {
        get => languages.Value is { } value ? SpecOptions.List(value).Contains(Code) : OptionCatalog.DefaultLanguages.Contains(Code);
        set
        {
            if (languages.Value is null)
            {
                languages.Value = string.Join(",", OptionCatalog.DefaultLanguages);
            }

            languages.Toggle(Code, value);
        }
    }

    public bool Matches(string? filter) =>
        string.IsNullOrWhiteSpace(filter) || new[] { Code, NativeName, EnglishName }.Any(t => t.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>A translation service as a card: what it costs, where the texts go and what it needs.</summary>
public sealed partial class TranslationServiceViewModel(OptionViewModel option, ChoiceViewModel choice, IReadOnlyList<string> pros, IReadOnlyList<string> cons) : ObservableObject
{
    public string Value => choice.Value;

    public string Label => choice.Label;

    public string Icon => choice.Icon;

    public string Text => choice.Text;

    public IReadOnlyList<string> Pros => [.. pros.Select(L.Get)];

    public IReadOnlyList<string> Cons => [.. cons.Select(L.Get)];

    public bool IsSelected => option.Value == Value;

    public void Select() => option.Value = Value;

    public void Refresh() => OnPropertyChanged(string.Empty);
}
