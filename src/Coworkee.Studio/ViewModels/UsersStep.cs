using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Localization;

namespace Coworkee.Studio.ViewModels;

/// <summary>Development users and roles the app is seeded with; with Keycloak the realm gets the same users and passwords.</summary>
public sealed partial class UsersStep(MainViewModel main) : StepViewModel(main)
{
    private bool _loading;

    protected override string TitleText => "Users & roles";

    protected override string SubtitleText => "Who can sign in right after the first start, and what they may do.";

    protected override string PitchText => "Every user gets an account with its password, generated unless you set one; with Keycloak the realm gets the same users and passwords. Roles bundle permissions of the chosen modules and of your entities.";

    public override string Icon => "Icon.AccountGroup";

    public ObservableCollection<RoleViewModel> Roles { get; } = [];

    public ObservableCollection<UserViewModel> Users { get; } = [];

    /// <summary>The administrator the template always seeds.</summary>
    public string Administrator => $"admin@{(Main.Basics.Name is { Length: > 0 } name ? name.Trim().ToLowerInvariant() : "myapp")}.local";

    public string Counts => L.Format("{0} roles · {1} users", Roles.Count + PermissionCatalog.BuiltInRoles(Main.ToSpec()).Count, Users.Count + 1);

    public IReadOnlyList<string> BuiltInRoles => PermissionCatalog.BuiltInRoles(Main.ToSpec());

    public override bool Owns(string error) => error.StartsWith("users[", StringComparison.Ordinal) || error.StartsWith("roles[", StringComparison.Ordinal);

    [RelayCommand]
    public void AddRole()
    {
        var role = new RoleViewModel(Changed) { Name = Unique(L.Get("Editor"), [.. Roles.Select(r => r.Name), .. BuiltInRoles]) };
        role.Offer(Permissions());
        Roles.Add(role);
        Changed();
    }

    [RelayCommand]
    public void RemoveRole(RoleViewModel role)
    {
        Roles.Remove(role);
        Changed();
    }

    [RelayCommand]
    public void AddUser()
    {
        var domain = Administrator.Split('@')[1];
        var taken = Users.Select(u => u.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var user = new UserViewModel(Changed) { Email = Enumerable.Range(1, 1000).Select(i => $"user{i}@{domain}").First(e => !taken.Contains(e)) };
        var roles = AvailableRoles();
        user.Offer(roles, roles.Take(1));
        Users.Add(user);
        Changed();
    }

    [RelayCommand]
    public void RemoveUser(UserViewModel user)
    {
        Users.Remove(user);
        Changed();
    }

    public List<RoleSpec> RoleSpecs() => [.. Roles.Select(r => r.ToSpec())];

    public List<UserSpec> UserSpecs() => [.. Users.Select(u => u.ToSpec())];

    public void Load(IEnumerable<RoleSpec> roles, IEnumerable<UserSpec> users)
    {
        _loading = true;
        Roles.Clear();
        Users.Clear();
        foreach (var spec in roles)
        {
            var role = new RoleViewModel(Changed) { Name = spec.Name, Description = spec.Description ?? string.Empty };
            Roles.Add(role);
        }

        foreach (var spec in users)
        {
            Users.Add(new UserViewModel(Changed) { Email = spec.Email, DisplayName = spec.DisplayName ?? string.Empty, Password = spec.Password ?? string.Empty, Admin = spec.Admin });
        }

        // the options and entities of the spec decide what can be checked, so they come in after the spec is complete
        var permissions = Permissions();
        var available = AvailableRoles();
        foreach (var (role, spec) in Roles.Zip(roles))
        {
            role.Offer(permissions, spec.Permissions);
        }

        foreach (var (user, spec) in Users.Zip(users))
        {
            user.Offer(available, spec.Roles);
        }

        _loading = false;
    }

    public override void Refresh()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        var permissions = Permissions();
        var roles = AvailableRoles();
        foreach (var role in Roles)
        {
            role.Offer(permissions);
        }

        foreach (var user in Users)
        {
            user.Offer(roles);
        }

        _loading = false;
        for (var i = 0; i < Roles.Count; i++)
        {
            Roles[i].Errors = [.. Errors.Where(e => e.StartsWith($"roles[{i}]", StringComparison.Ordinal))];
        }

        for (var i = 0; i < Users.Count; i++)
        {
            Users[i].Errors = [.. Errors.Where(e => e.StartsWith($"users[{i}]", StringComparison.Ordinal))];
        }

        OnPropertyChanged(nameof(Counts));
        OnPropertyChanged(nameof(Administrator));
        OnPropertyChanged(nameof(BuiltInRoles));
    }

    private IReadOnlyList<string> Permissions() => PermissionCatalog.For(Main.ToSpec());

    private List<string> AvailableRoles() => [.. BuiltInRoles, .. Roles.Select(r => r.Name.Trim()).Where(n => n.Length > 0)];

    private void Changed()
    {
        if (!_loading)
        {
            Main.SpecChanged();
        }
    }

    private static string Unique(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 100).Select(i => i == 1 ? name : $"{name} {i}").First(n => !used.Contains(n));
    }
}
