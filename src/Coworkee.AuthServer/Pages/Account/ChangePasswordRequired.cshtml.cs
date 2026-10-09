using Coworkee.Account;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.Pages.Account;

/// <summary>After the sign-in and before the app gets tokens: the administrator asked for a new password, or it expired.</summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ChangePasswordRequiredModel(UserManager<User> users, SignInManager<User> signIn, CoworkeeDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public Manage.ChangePasswordModel.ChangePasswordInput Input { get; set; } = new();

    public bool Expired { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        Expired = !user.MustChangePassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        Expired = !user.MustChangePassword;
        if (Input.NewPassword != Input.ConfirmPassword)
        {
            Errors = [T("The new passwords do not match.")];
            return Page();
        }

        if (Input.NewPassword == Input.CurrentPassword)
        {
            Errors = [T("Choose a password other than the current one.")];
            return Page();
        }

        var result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            Errors = result.Errors.Select(e => e.Code == "PasswordMismatch" ? T("The current password is not correct.") : T(e.Description)).ToList();
            return Page();
        }

        await db.SaveChangesAsync();
        await signIn.RefreshSignInAsync(user);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }
}
