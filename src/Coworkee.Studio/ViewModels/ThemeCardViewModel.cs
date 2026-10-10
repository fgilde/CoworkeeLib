using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

public sealed partial class ThemeCardViewModel(ThemeInfo theme) : ObservableObject
{
    public ThemeInfo Theme => theme;

    public string Name => theme.Name == "HighContrast" ? "High Contrast" : theme.Name;

    public string Description => L.Get(OptionCatalog.Find("theme")!.Choices!.FirstOrDefault(c => c.Value == theme.Name)?.Description ?? string.Empty);

    public PreviewViewModel Light { get; } = new(theme.Light, theme.Radius, theme.Font, theme.Name, null, false);

    public PreviewViewModel Dark { get; } = new(theme.Dark, theme.Radius, theme.Font, theme.Name, null, true);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
