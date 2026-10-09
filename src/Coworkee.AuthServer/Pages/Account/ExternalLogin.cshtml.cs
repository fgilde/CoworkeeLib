using Coworkee.AuthServer.External;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class ExternalLoginModel(SignInManager<User> signIn, ExternalSignIn external, CoworkeeDbContext db) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public string? ReturnUrl { get; private set; }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl, string? remoteError)
    {
        ReturnUrl = returnUrl;
        var login = remoteError is null ? await signIn.GetExternalLoginInfoAsync() : null;
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (login is null)
        {
            ErrorMessage = AuthTexts.T("The sign-in provider did not confirm the sign-in.");
            return Page();
        }

        var user = await external.FindOrCreateAsync(login, HttpContext.RequestAborted);
        if (!user.IsSuccess)
        {
            ErrorMessage = user.Error!.Message;
            return Page();
        }

        await signIn.SignInAsync(user.Value, isPersistent: false, login.LoginProvider);
        user.Value.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
