using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Asks where the users of a role go: target roles, copy or move, all users or the chosen ones.</summary>
public partial class RoleUsersDialog
{
    private IReadOnlyList<RoleDto> _roles = [];
    private List<Guid> _targets = [];
    private readonly List<UserDto> _users = [];
    private bool _move;
    private bool _some;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public RoleDto Role { get; set; } = null!;

    /// <summary>The request for <see cref="ICoworkeeApi.TransferRoleUsersAsync"/>; <c>null</c> when cancelled.</summary>
    public static async Task<TransferRoleUsersRequest?> ShowAsync(IDialogService dialogs, string title, RoleDto role)
    {
        var dialog = await dialogs.ShowExAsync<RoleUsersDialog>(title, new DialogParameters { [nameof(Role)] = role }, EditDialogExtensions.Small());
        return await dialog.Result is { Canceled: false, Data: TransferRoleUsersRequest request } ? request : null;
    }

    protected override async Task OnInitializedAsync() => await Snackbar.RunAsync(async () => _roles = await Api.GetRolesAsync());

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private static string Describe(UserDto? user) => user is null ? string.Empty : $"{user.FirstName} {user.LastName} <{user.Email}>".Trim();

    private async Task<IEnumerable<UserDto>> SearchAsync(string? search, CancellationToken cancellationToken) =>
        (await Api.GetRoleUsersAsync(Role.Id, new PageRequest(1, 50, search), cancellationToken)).Items.Where(u => _users.All(c => c.Id != u.Id));

    private void Add(UserDto? user)
    {
        if (user is not null && _users.All(u => u.Id != user.Id))
        {
            _users.Add(user);
        }
    }

    private void Ok() => Dialog.Close(DialogResult.Ok(new TransferRoleUsersRequest(_targets, _move, _some ? [.. _users.Select(u => u.Id)] : null)));
}
