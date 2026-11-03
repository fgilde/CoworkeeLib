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
