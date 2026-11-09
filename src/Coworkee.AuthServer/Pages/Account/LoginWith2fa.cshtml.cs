using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LoginWith2faModel(SignInManager<User> signIn, CoworkeeDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    [BindProperty]
    public CodeInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        return await signIn.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("Login") : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsActive)
        {
            return RedirectToPage("Login");
        }

        var code = Input.Code.Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = Input.UseRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
            : await signIn.TwoFactorAuthenticatorSignInAsync(code.Replace("-", string.Empty, StringComparison.Ordinal), RememberMe, rememberClient: false);
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        if (result.Succeeded && !AuthSessions.Refused(HttpContext))
        {
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
        }

        ErrorMessage = AuthTexts.T(result.Succeeded ? "You are signed in on too many devices. Sign out on one of them first or ask an administrator to end a session."
            : result.IsLockedOut ? "Too many attempts. Try again later." : "The code is not valid.");
        return Page();
    }

    public sealed class CodeInput
    {
        public string Code { get; set; } = string.Empty;

        public bool UseRecoveryCode { get; set; }
    }
}
