using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The sign-in and registration configuration in one place, through the forms of the settings page: the setting groups Security and Account, the
/// registration part of the app settings and the sign-in design (card and its texts). Nothing is stored here differently.
/// </summary>
public partial class SignInConfiguration
{
    private static readonly IReadOnlySet<string> SettingGroups = new HashSet<string>(StringComparer.Ordinal) { "Security", "Account" };

    private static readonly IReadOnlyCollection<string> RegistrationOnly = [nameof(CoworkeeAppSettings.Registration)];

    private static readonly IReadOnlyCollection<string> DesignTextsOnly = [nameof(SignInDesign.ShowAppName), nameof(SignInDesign.Texts)];

    private static readonly ClientAppConfiguration DesignSection = new(SignInDesign.Section, "Sign-in page", typeof(SignInDesign), null);

    private bool _system = true;

    [Inject] private IEnumerable<ClientAppConfiguration> Registrations { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    // other organisations change their own session limits; everything else is system wide
    private SettingScope Scope => _system ? SettingScope.Global : SettingScope.Tenant;

    // the app settings carry the registration when they derive from CoworkeeAppSettings
    private ClientAppConfiguration? RegistrationSection =>
        Registrations.FirstOrDefault(r => r.IsAppSettings && typeof(CoworkeeAppSettings).IsAssignableFrom(r.Type));

    protected override async Task OnInitializedAsync() =>
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
}
