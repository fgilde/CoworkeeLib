using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>Status of the Keycloak login theme the API keeps in the app's design, with a button to write it now; hidden unless the API has a theme folder.</summary>
public partial class KeycloakThemeCard
{
    private KeycloakThemeStatusDto? _status;
    private bool _applying;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _status = await Api.GetKeycloakThemeStatusAsync();
        }
        catch (ApiException)
        {
            _status = null;
        }
    }

    private async Task ApplyAsync()
    {
        _applying = true;
        try
        {
            KeycloakThemeStatusDto? applied = null;
            if (await Snackbar.RunAsync(async () => applied = await Api.ApplyKeycloakThemeAsync()) && applied is not null)
            {
                _status = applied;
                if (applied.Error is null)
                {
                    Snackbar.Add(L["Keycloak sign-in page updated"], Severity.Success);
                }
            }
        }
        finally
        {
            _applying = false;
        }
    }
}
