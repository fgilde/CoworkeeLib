using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class ResourcePermissionsPanel
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<ResourcePermissionDto>? _entries;
    private Dictionary<Guid, string> _roles = [];
    private Dictionary<Guid, string> _users = [];
    private Dictionary<Guid, string> _groups = [];
    private string? _principal;
    private Guid _role;

    [Parameter, EditorRequired] public string ResourceType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid ResourceId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var roles = Api.GetRolesAsync();
        var users = Api.GetUsersAsync(new PageRequest(1, 200));
        var groups = Api.GetGroupsAsync(new PageRequest(1, 200));
        _roles = (await roles).Where(r => !r.IsSystem).ToDictionary(r => r.Id, r => r.Name);
        _users = (await users).Items.ToDictionary(u => u.Id, u => u.Email);
        _groups = (await groups).Items.ToDictionary(g => g.Id, g => g.Name);
        await ReloadAsync();
    }

    private async Task ReloadAsync() => _entries = await Api.GetResourcePermissionsAsync(ResourceType, ResourceId);

    private string PrincipalName(ResourcePermissionDto entry) => entry.PrincipalType == PrincipalType.User
        ? _users.GetValueOrDefault(entry.PrincipalId) ?? entry.PrincipalId.ToString()
        : (_groups.GetValueOrDefault(entry.PrincipalId) ?? entry.PrincipalId.ToString()) + " (group)";

    private async Task GrantAsync()
    {
        var parts = _principal!.Split(':');
        try
        {
            await Api.GrantResourcePermissionAsync(ResourceType, ResourceId, new GrantResourcePermissionRequest(Enum.Parse<PrincipalType>(parts[0]), Guid.Parse(parts[1]), _role));
            await ReloadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task RevokeAsync(ResourcePermissionDto entry)
    {
        try
        {
            await Api.RevokeResourcePermissionAsync(ResourceType, ResourceId, entry.Id);
            await ReloadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}
