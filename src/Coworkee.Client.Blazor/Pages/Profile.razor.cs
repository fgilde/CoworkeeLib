using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

public partial class Profile
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private ProfileDto? _profile;
    private string? _firstName;
    private string? _lastName;
    private string? _phone;
    private string? _manage;
    private bool _busy;

    private string DisplayName => $"{_firstName} {_lastName}".Trim() is { Length: > 0 } name ? name : _profile!.Email;

    private string Initials => string.Concat(DisplayName.Split(' ', '@', '.').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    protected override async Task OnInitializedAsync()
    {
        _manage = (await AuthenticationState).User.FindFirst("manage_url")?.Value?.TrimEnd('/');
        Show(await Api.GetMyProfileAsync());
    }

    private void Show(ProfileDto profile)
    {
        _profile = profile;
        (_firstName, _lastName, _phone) = (profile.FirstName, profile.LastName, profile.PhoneNumber);
    }

    private async Task SaveAsync()
    {
        _busy = true;
        try
        {
            Show(await Api.UpdateMyProfileAsync(new UpdateProfileRequest(_firstName, _lastName, _phone)));
            Snackbar.Add("Profile saved.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }
}
