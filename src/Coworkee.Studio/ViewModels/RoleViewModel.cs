using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

public sealed partial class RoleViewModel(Action changed) : SeedItemViewModel(changed)
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    public ObservableCollection<Choice> Permissions { get; } = [];

    public void Offer(IReadOnlyList<string> permissions, IEnumerable<string>? checkedNames = null) => Offer(Permissions, permissions, checkedNames);

    public RoleSpec ToSpec() => new()
    {
        Name = Name.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Permissions = [.. Permissions.Where(p => p.IsChecked).Select(p => p.Name)],
    };
}
