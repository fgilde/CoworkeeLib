using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Settings
{
    private bool _system = true;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    // system wide settings change only from the system organisation
    protected override async Task OnInitializedAsync() =>
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
}
