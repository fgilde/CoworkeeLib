using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

public sealed partial class LayoutCardViewModel(string value, string description) : ObservableObject
{
    public string Value => value;

    public string Title => L.Get(value);

    public string Description => L.Get(description);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
