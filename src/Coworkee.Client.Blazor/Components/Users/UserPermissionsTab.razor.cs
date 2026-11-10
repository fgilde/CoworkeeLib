using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Users;

/// <summary>
/// What a user holds and where it comes from (role, role through a group, group, direct, computed), with a checkbox per permission to grant it to him
/// directly (<c>Identity.Permissions.Manage</c>). Only grants: what his roles and groups give is changed there.
/// </summary>
public partial class UserPermissionsTab
{
    private readonly PendingToggles<string> _toggles = new();
    private HashSet<string> _granted = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<PermissionSourceDto>> _held = new(StringComparer.Ordinal);
    private IReadOnlyList<PermissionGroupDto>? _definitions;
    private bool _canGrant;
    private bool _heldOnly = true;
    private string? _filter;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    protected override async Task PrepareAsync()
    {
        _canGrant = await MayAsync(IdentityPermissions.Permissions.Manage);
        // who grants needs the permissions the user does not hold yet
        _heldOnly = !_canGrant;
        // the names of the permissions need Roles.View; without, the codes stand alone
        if (await MayAsync(IdentityPermissions.Roles.View))
        {
            await Snackbar.RunAsync(async () => _definitions = await Api.GetPermissionDefinitionsAsync());
        }
    }

    protected override async Task LoadAsync()
    {
        var permissions = await Api.GetUserPermissionsAsync(UserId);
        _granted = new HashSet<string>(permissions.Granted, StringComparer.Ordinal);
        _held = permissions.Permissions.ToDictionary(p => p.Name, p => p.Sources, StringComparer.Ordinal);
    }

    private bool Granted(string name) => _toggles.Shows(name, _granted.Contains(name));

    private IEnumerable<Row> Rows()
    {
        var defined = (_definitions ?? []).SelectMany(g => g.Permissions.Select(p => new Row(p.Name, p.DisplayName, g.DisplayName))).ToList();
        var known = defined.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        return defined.Concat(_held.Keys.Union(_granted).Where(n => !known.Contains(n)).Order(StringComparer.Ordinal).Select(n => new Row(n, n, string.Empty)))
            .Where(r => !_heldOnly || _held.ContainsKey(r.Name) || Granted(r.Name))
            .Where(r => string.IsNullOrWhiteSpace(_filter)
                        || r.DisplayName.Contains(_filter.Trim(), StringComparison.CurrentCultureIgnoreCase)
                        || r.Name.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private string Describe(PermissionSourceDto source) => source switch
    {
        { Kind: PermissionSourceKind.Role, GroupName: { } group } => L["Role {0} through group {1}", source.Name ?? string.Empty, group],
        { Kind: PermissionSourceKind.Role } => L["Role {0}", source.Name ?? string.Empty],
        { Kind: PermissionSourceKind.Group } => L["Group {0}", source.Name ?? string.Empty],
        { Kind: PermissionSourceKind.Direct } => L["Granted directly"],
        _ => L["Computed"],
    };

    private static string Icon(PermissionSourceDto source) => source.Kind switch
    {
        PermissionSourceKind.Role => Icons.Material.Outlined.Shield,
        PermissionSourceKind.Group => Icons.Material.Outlined.Groups,
        PermissionSourceKind.Direct => Icons.Material.Outlined.Person,
        _ => Icons.Material.Outlined.Functions,
    };

    private string? Link(PermissionSourceDto source) => source.Kind switch
    {
        PermissionSourceKind.Role => Links.Href($"/admin/roles/{source.Id}"),
        PermissionSourceKind.Group => Links.Href($"/admin/groups?group={source.Id}"),
        _ => null,
    };

    // the sources change with the grant: read them again once it is saved
    private void Toggle(Row row, bool on) =>
        _ = _toggles.ToggleAsync(row.Name, on,
            () => on ? Api.AddGrantAsync(PermissionProviderType.User, UserId, row.Name) : Api.RemoveGrantAsync(PermissionProviderType.User, UserId, row.Name),
            async failure =>
            {
                await SettledAsync(failure, row.DisplayName, () =>
                {
                    if (on)
                    {
                        _granted.Add(row.Name);
                    }
                    else
                    {
                        _granted.Remove(row.Name);
                    }
                });
                if (failure is null)
                {
                    await ReloadAsync();
                    await InvokeAsync(StateHasChanged);
                }
            });

    private sealed record Row(string Name, string DisplayName, string Group);
}
