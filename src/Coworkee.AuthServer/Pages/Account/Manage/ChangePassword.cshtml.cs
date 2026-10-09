using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account.Manage;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ChangePasswordModel(UserManager<User> users, SignInManager<User> signIn, CoworkeeDbContext db) : PageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    public bool Done { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.GetUserAsync(User) is not { } user)
        {
            return Challenge();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            Errors = [AuthTexts.T("The new passwords do not match.")];
            return Page();
        }

        var result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            Errors = result.Errors.Select(e => e.Code == "PasswordMismatch" ? AuthTexts.T("The current password is not correct.") : e.Description).ToList();
            return Page();
        }

        // the store leaves saving to the unit of work
        await db.SaveChangesAsync();

        // the security stamp changed: keep this session, other sessions fall out at their next check
        await signIn.RefreshSignInAsync(user);
        Done = true;
        return Page();
    }

    public sealed class ChangePasswordInput
    {
        public string CurrentPassword { get; set; } = string.Empty;

        public string NewPassword { get; set; } = string.Empty;

        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
