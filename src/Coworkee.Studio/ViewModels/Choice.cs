using CommunityToolkit.Mvvm.ComponentModel;

namespace Coworkee.Studio.ViewModels;

/// <summary>A permission of a role or a role of a user, as a check box.</summary>
public sealed partial class Choice(string name, bool isChecked, Action changed) : ObservableObject
{
    public string Name => name;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}
