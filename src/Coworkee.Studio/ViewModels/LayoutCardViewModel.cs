using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class LayoutCardViewModel(string value, string description) : ObservableObject
{
    public string Value => value;

    public string Title => L.Get(value);

    public string Description => L.Get(description);

    public bool IsCard => value == "Card";

    public bool IsSplit => value == "Split";

    public bool IsSlide => value == "Slide";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
