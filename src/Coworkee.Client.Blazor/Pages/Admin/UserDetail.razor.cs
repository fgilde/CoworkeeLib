using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.People;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class UserDetail
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private UserCards Cards { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public Guid Id { get; set; }

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = null!;

    [Inject] private IAuthorizationService Authorization { get; set; } = null!;

    private UserDetailDto? _user;
    private UserForm _form = new();
    private ObjectEditMeta<UserForm>? _meta;
    private string? _language;
    private bool _canManage;
    private bool _busy;
    private bool _mayImpersonate;
    private string? _error;

    protected override async Task OnParametersSetAsync()
    {
        var user = (await AuthenticationState).User;
        _mayImpersonate = Impersonation.Offers(user, Id);
        _canManage = (await Authorization.AuthorizeAsync(user, Security.PermissionPolicy.For(IdentityPermissions.Users.Manage))).Succeeded;
        try
        {
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 404 ? L["This user does not exist."] : exception.Message;
        }
    }

    private async Task LoadAsync()
    {
        _user = await Api.GetUserDetailAsync(Id);
        _language = (await Api.GetUserLanguageAsync(Id))?.Culture;
        _form = UserForm.From(_user, _language);
        _meta = _form.ObjectEditMeta(meta => UserFormMeta.Apply(meta, L, !_canManage));
    }

    private static string DisplayName(UserDetailDto user) =>
        $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Email;

    private Task SaveAsync(EditContext context) => RunAsync(async () =>
    {
        await Api.UpdateUserAsync(Id, _form.ToRequest());
        var language = _form.Language.Length == 0 ? null : _form.Language;
        if (language != _language)
        {
            await Api.SetUserLanguageAsync(Id, language);
        }

        Cards.Forget(Id);
    }, L["Saved"]);

    private async Task UnlockAsync()
    {
        if (await UserActions.UnlockAsync(Api, Snackbar, L, Id))
        {
            await Snackbar.RunAsync(LoadAsync);
        }
    }

    private async Task UploadAvatarAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (await AvatarPicture.ReadAsync(JS, file) is not { } picture)
        {
            Snackbar.Add(L["This file is no picture the browser can show."], Severity.Warning);
            return;
        }

        await RunAsync(async () =>
        {
            await Api.SetUserAvatarAsync(Id, picture);
            Cards.Forget(Id);
        }, L["Picture saved"]);
    }

    private Task RemoveAvatarAsync() => RunAsync(async () =>
    {
        await Api.SetUserAvatarAsync(Id, null);
        Cards.Forget(Id);
    }, L["Picture removed"]);

    private async Task LockAsync()
    {
        if (await UserActions.LockAsync(Dialogs, Api, Snackbar, L, Id, _user!.Email))
        {
            await Snackbar.RunAsync(LoadAsync);
        }
    }

    private async Task SignOutAsync()
    {
        if (await UserActions.SignOutAsync(Dialogs, Api, Snackbar, L, Id, _user!.Email))
        {
            await Snackbar.RunAsync(LoadAsync);
        }
    }

    private Task ImpersonateAsync() => Impersonation.StartAsync(Dialogs, Api, Snackbar, Nav, L, Id, _user!.Email);

    private async Task DeleteAsync()
    {
        if (await UserActions.DeleteAsync(Dialogs, Api, Snackbar, L, Id, _user!.Email))
        {
            Nav.NavigateTo(Links.Href("/admin/users")!);
        }
    }

    private async Task RunAsync(Func<Task> action, string done)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await action();
            Snackbar.Add(done, Severity.Success);
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }
}
