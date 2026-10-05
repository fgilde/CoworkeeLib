using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Groups
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<GroupDto> _groups = [];
    private IReadOnlyList<UserDto> _users = [];
    private IReadOnlyList<RoleDto> _roles = [];
    private string? _name;

    protected override async Task OnInitializedAsync()
    {
        _users = (await Api.GetUsersAsync(new PageRequest(1, 200))).Items;
        _roles = await Api.GetRolesAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync() => _groups = (await Api.GetGroupsAsync(new PageRequest(1, 200))).Items;

    private string UserName(Guid id) => _users.FirstOrDefault(u => u.Id == id)?.Email ?? string.Empty;

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private Task CreateAsync() => RunAsync(async () =>
    {
        await Api.CreateGroupAsync(new GroupRequest(_name!, null));
        _name = null;
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }

        await ReloadAsync();
    }
}
