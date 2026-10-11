using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.ViewModels;

/// <summary>A permission of a role or a role of a user, as a check box.</summary>
public sealed partial class Choice(string name, bool isChecked, Action changed) : ObservableObject
{
    public string Name => name;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Base of roles and users: their validation messages and check boxes that follow what the app offers.</summary>
public abstract partial class SeedItemViewModel(Action changed) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial IReadOnlyList<string> Errors { get; set; } = [];

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Offers <paramref name="available"/> with <paramref name="checkedNames"/> (default: the checked ones) checked; untouched when nothing changed.</summary>
    protected void Offer(ObservableCollection<Choice> choices, IReadOnlyList<string> available, IEnumerable<string>? checkedNames)
    {
        if (checkedNames is null && choices.Select(c => c.Name).SequenceEqual(available))
        {
            return;
        }

        var current = (checkedNames ?? choices.Where(c => c.IsChecked).Select(c => c.Name)).ToHashSet(StringComparer.Ordinal);
        choices.Clear();
        foreach (var name in available.Union(current))
        {
            choices.Add(new Choice(name, current.Contains(name), changed));
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Errors) or nameof(HasErrors)))
        {
            changed();
        }
    }
}

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
