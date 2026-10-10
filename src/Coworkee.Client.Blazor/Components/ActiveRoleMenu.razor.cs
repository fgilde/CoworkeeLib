using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Lets a user with several roles act within one of them; hidden when the system setting is off or there is nothing to choose.</summary>
public partial class ActiveRoleMenu : IDisposable
{
    private MyRolesDto? _roles;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [Inject] private MudBlazor.ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    private MyRoleDto? Active => _roles?.Roles.FirstOrDefault(r => r.Id == _roles.ActiveRoleId);

    protected override async Task OnInitializedAsync()
    {
        Permissions.Changed += Reload;
        await LoadAsync();
    }

    public void Dispose() => Permissions.Changed -= Reload;

    private void Reload() => _ = InvokeAsync(async () =>
    {
        await LoadAsync();
        StateHasChanged();
    });

    private async Task LoadAsync()
    {
        try
        {
            _roles = await Api.GetMyRolesAsync();
        }
        catch (ApiException)
        {
            _roles = null;
        }
    }

    // reloads at once; the server's push reaches the other tabs
    private async Task SwitchAsync(Guid? roleId)
    {
        if (await Snackbar.RunAsync(() => Api.SetActiveRoleAsync(roleId)))
        {
            Permissions.Reset();
        }
    }
}
