using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

public sealed partial class UserViewModel(Action changed) : SeedItemViewModel(changed)
{
    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Admin { get; set; }

    public ObservableCollection<Choice> Roles { get; } = [];

    public void Offer(IReadOnlyList<string> roles, IEnumerable<string>? checkedNames = null) => Offer(Roles, roles, checkedNames);

    public UserSpec ToSpec() => new()
    {
        Email = Email.Trim(),
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
        Password = string.IsNullOrEmpty(Password) ? null : Password,
        Admin = Admin,
        Roles = [.. Roles.Where(r => r.IsChecked).Select(r => r.Name)],
    };
}
