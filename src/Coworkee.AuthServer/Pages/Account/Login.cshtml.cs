using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LoginModel(SignInManager<User> signIn, UserManager<User> users, CoworkeeDbContext db) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null || !user.IsActive)
        {
            ErrorMessage = "Email or password is not correct.";
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        await db.SaveChangesAsync();
        if (result.IsLockedOut)
        {
            ErrorMessage = "Too many attempts. Try again later.";
            return Page();
        }

        if (!result.Succeeded)
        {
            ErrorMessage = "Email or password is not correct.";
            return Page();
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    public sealed class LoginInput
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
