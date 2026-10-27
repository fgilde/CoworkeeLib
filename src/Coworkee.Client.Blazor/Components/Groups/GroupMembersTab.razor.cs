using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Groups;

/// <summary>The members of a group as a users table: add through a user picker, remove, move all into another group.</summary>
public partial class GroupMembersTab
{
    private static readonly string[] SearchFields = [nameof(UserRow.Email), nameof(UserRow.FirstName), nameof(UserRow.LastName)];
    private CoworkeeDataTable<UserRow>? _table;
    private bool? _canViewUsers;
    private GroupRow? _shown;
    private HashSet<Guid> _owners = [];

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public GroupRow Group { get; set; } = null!;

    /// <summary>The user may manage members (<c>Identity.Groups.ManageMembers</c>).</summary>
    [Parameter] public bool CanManage { get; set; }

    [Parameter] public EventCallback Changed { get; set; }

    // the server resolves the membership: the URL stays the same size however many members the group has
    private string Filter => $"Groups/any(m: m/GroupId eq {Group.Id})";

    // the members are rows of the Users set, which Groups.View alone does not open
    protected override async Task OnInitializedAsync() => _canViewUsers = await Permissions.HasAsync(IdentityPermissions.Users.View);

    // the page loads the group again after every change, also those of others
    protected override async Task OnParametersSetAsync()
    {
        if (_canViewUsers == true && !ReferenceEquals(_shown, Group))
        {
            _shown = Group;
            var query = new ODataQuery { Filter = $"Groups/any(m: m/GroupId eq {Group.Id} and m/IsOwner)", Select = nameof(UserRow.Id), Count = false };
            _owners = [.. (await OData.QueryAsync<UserRow>("Users", query)).Items.Select(u => u.Id)];
        }
    }

    private async Task AddAsync()
    {
        if (await Dialogs.ShowEditAsync(L["Add member"], new MemberForm(), Configure) is { UserId: var userId } && userId != Guid.Empty)
        {
            await RunAsync(() => Api.AddGroupMembersAsync(Group.Id, [userId]));
        }
    }

    private bool IsOwner(UserRow user) => _owners.Contains(user.Id);

    private Task ToggleOwnerAsync(UserRow user)
    {
        var owners = _owners.ToList();
        if (!owners.Remove(user.Id))
        {
            owners.Add(user.Id);
        }

        return RunAsync(() => Api.SetGroupOwnersAsync(Group.Id, owners));
    }

    private Task RemoveAsync(UserRow user) => RunAsync(() => Api.RemoveGroupMembersAsync(Group.Id, [user.Id]));

    private async Task MoveAllAsync()
    {
        if (await GroupMoveDialog.PickAsync(Dialogs, L["Move all members of {0} to", Group.Name], Group.Id, allowRoot: false, excludeSubtree: false) is { Id: { } to })
        {
            await RunAsync(() => Api.MoveGroupMembersAsync(Group.Id, to));
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (await Snackbar.RunAsync(action))
        {
            // the filter stays the same, so the table reloads here; changes of others arrive on the group's entity topic
            if (_table is not null)
            {
                await _table.ReloadAsync();
            }

            await Changed.InvokeAsync();
        }
    }

#pragma warning disable BL0005 // MudEx configures the editor through these instances
    private void Configure(ObjectEditMeta<MemberForm> meta) =>
        meta.Property(m => m.UserId).WithLabel(L["User"])
            .RenderWith<ODataPicker, Guid>(p => p.Value)
            .WithAdditionalAttribute(nameof(ODataPicker.EntitySet), "Users")
            .WithAdditionalAttribute(nameof(ODataPicker.TextProperty), "FirstName,LastName,Email")
            .WithAdditionalAttribute(nameof(ODataPicker.Required), true);
#pragma warning restore BL0005

    private sealed class MemberForm
    {
        public Guid UserId { get; set; }
    }
}
