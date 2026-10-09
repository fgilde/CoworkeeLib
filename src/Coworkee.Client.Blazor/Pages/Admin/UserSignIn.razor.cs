using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>How a user signs in, for administrators: address, password, two-step verification and external sign-ins.</summary>
public partial class UserSignIn
{
    [Parameter, EditorRequired] public UserDetailDto User { get; set; } = null!;

    [Parameter] public bool CanManage { get; set; }

    /// <summary>Something changed; the page loads the user again.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private IReadOnlyList<UserLoginDto> Logins => User.Logins ?? [];

    private async Task ChangeEmailAsync()
    {
        if (await Dialogs.ShowEditAsync(L["Change email of {0}", User.Email], new UserEmailForm { Email = User.Email }, f => Api.SetUserEmailAsync(User.Id, f.ToRequest()), EmailMeta))
        {
            await DoneAsync(L["Email changed"]);
        }
    }

    private async Task SetPasswordAsync()
    {
        if (await Dialogs.ShowEditAsync(L["Set password of {0}", User.Email], new UserPasswordForm(), f => Api.SetUserPasswordAsync(User.Id, f.ToRequest()), PasswordMeta))
        {
            await DoneAsync(L["Password set; the user was signed out everywhere."]);
        }
    }

    private Task SendResetAsync() => RunAsync(() => Api.SendPasswordResetAsync(User.Id), L["Password reset mail queued for {0}.", User.Email]);

    private Task InviteAsync() => RunAsync(() => Api.SendInvitationAsync(User.Id), L["Invitation sent to {0}.", User.Email]);

    private async Task ResetTwoFactorAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Reset two-step verification"], L["Turn two-step verification of {0} off? The user can set it up again.", User.Email], L["Reset"], L["Cancel"],
                Icons.Material.Outlined.Security))
        {
            await RunAsync(() => Api.ResetUserTwoFactorAsync(User.Id), L["Two-step verification was reset."]);
        }
    }

    private async Task RemoveLoginAsync(UserLoginDto login)
    {
        if (await Dialogs.ConfirmAsync(L["Remove external sign-in"], L["Remove the sign-in with {0} from {1}?", login.DisplayName ?? login.LoginProvider, User.Email], L["Remove"], L["Cancel"],
                Icons.Material.Outlined.LinkOff))
        {
            await RunAsync(() => Api.RemoveUserLoginAsync(User.Id, login), L["External sign-in removed."]);
        }
    }

    private void EmailMeta(ObjectEditMeta<UserEmailForm> meta)
    {
        meta.Property(f => f.Email).WithLabel(L["New email address"]).WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Email);
        meta.Property(f => f.Confirmed).WithLabel(L["Confirmed (otherwise a confirmation mail is sent)"]);
    }

    private void PasswordMeta(ObjectEditMeta<UserPasswordForm> meta)
    {
        meta.Property(f => f.Password).WithLabel(L["New password"]).WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Password);
        meta.Property(f => f.MustChangePassword).WithLabel(L["Change password at next sign-in"]);
    }

    private async Task RunAsync(Func<Task> action, string done)
    {
        if (await Snackbar.RunAsync(action, done))
        {
            await Changed.InvokeAsync();
        }
    }

    private async Task DoneAsync(string message)
    {
        Snackbar.Add(message, Severity.Success);
        await Changed.InvokeAsync();
    }
}
