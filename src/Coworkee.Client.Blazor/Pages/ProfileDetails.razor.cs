using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.People;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages;

public partial class ProfileDetails
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int AvatarPixels = 256;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    private ProfileDto? _profile;
    private ProfileForm _form = new();
    private ObjectEditMeta<ProfileForm>? _meta;
    private Guid _userId;
    private bool _busy;

    private string DisplayName => $"{_profile?.FirstName} {_profile?.LastName}".Trim() is { Length: > 0 } name ? name : _profile!.Email;

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthenticationState).User;
        _userId = Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
        Show(await Api.GetMyProfileAsync());
    }

    private void Show(ProfileDto profile)
    {
        _profile = profile;
        _form = ProfileForm.From(profile);
        _meta = _form.ObjectEditMeta(Configure);
    }

    private void Configure(ObjectEditMeta<ProfileForm> meta)
    {
        string details = L["Profile details"], address = L["Address"];
        meta.Property(p => p.FirstName).WithLabel(L["First name"]).WithGroup(details).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(p => p.LastName).WithLabel(L["Last name"]).WithGroup(details).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(p => p.PhoneNumber).WithLabel(L["Phone"]).WithGroup(details).WithAdditionalAttribute("MaxLength", 50);
        meta.Property(p => p.Street).WithLabel(L["Street"]).WithGroup(address).WithAdditionalAttribute("MaxLength", 200);
        meta.Property(p => p.ZipCode).WithLabel(L["Zip code"]).WithGroup(address).WithAdditionalAttribute("MaxLength", 20);
        meta.Property(p => p.City).WithLabel(L["City"]).WithGroup(address).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(p => p.Country).WithLabel(L["Country"]).WithGroup(address).WithAdditionalAttribute("MaxLength", 100);
    }

    private Task SaveAsync(EditContext context) => RunAsync(async () =>
    {
        Show(await Api.UpdateMyProfileAsync(_form.ToRequest()));
        Cards.Forget(_userId);
        Snackbar.Add(L["Profile saved"], Severity.Success);
    });

    private Task UploadAsync(IBrowserFile? file) => file is null ? Task.CompletedTask : RunAsync(async () =>
    {
        await using var stream = file.OpenReadStream(MaxUploadBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
        var resized = await module.InvokeAsync<string?>("resizeImage", $"data:{file.ContentType};base64,{Convert.ToBase64String(memory.ToArray())}", AvatarPixels);
        if (resized is null)
        {
            Snackbar.Add(L["This file is no picture the browser can show."], Severity.Warning);
            return;
        }

        Show(await Api.SetMyAvatarAsync(resized));
        Cards.Forget(_userId);
        Snackbar.Add(L["Picture saved"], Severity.Success);
    });

    private Task RemoveAsync() => RunAsync(async () =>
    {
        Show(await Api.SetMyAvatarAsync(null));
        Cards.Forget(_userId);
    });

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action);
        }
        finally
        {
            _busy = false;
        }
    }
}
