using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Settings
{
    private bool _system = true;

    [Inject] private IEnumerable<ClientAppConfiguration> Registrations { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    private IEnumerable<ClientAppConfiguration> Sections => Registrations.OrderByDescending(r => r.IsAppSettings);

    // typed and system wide settings change only from the system organisation
    protected override async Task OnInitializedAsync() =>
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
}
