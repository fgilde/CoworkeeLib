using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages;

public partial class ProfileSecurity
{
    private string? _manage;
    private ProfileDto? _profile;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        _manage = (await AuthenticationState).User.FindFirst("manage_url")?.Value?.TrimEnd('/');
        await Snackbar.RunAsync(async () => _profile = await Api.GetMyProfileAsync());
    }

    private async Task ChangeEmailAsync()
    {
        var hasPassword = _profile!.HasPassword;
        string? requested = null;
        if (await Dialogs.ShowEditAsync(L["Change email"], new ChangeEmailForm(), async form =>
            {
                await Api.ChangeMyEmailAsync(form.ToRequest());
                requested = form.NewEmail;
            }, meta => Configure(meta, hasPassword)))
        {
            Snackbar.Add(L["Open the link we sent to {0} to use the new address.", requested ?? string.Empty], Severity.Success);
        }
    }

    private void Configure(ObjectEditMeta<ChangeEmailForm> meta, bool hasPassword)
    {
        meta.Property(f => f.NewEmail).WithLabel(L["New email address"]).WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Email);
        var password = meta.Property(f => f.CurrentPassword).WithLabel(L["Current password"])
            .WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Password);
        if (!hasPassword)
        {
            password.Ignore();
        }
    }
}
