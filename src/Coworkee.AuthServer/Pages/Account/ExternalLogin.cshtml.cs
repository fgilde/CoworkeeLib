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
        var user = login is null ? null : await external.FindOrCreateAsync(login, HttpContext.RequestAborted);
        if (user?.Error?.Code == ExternalSignIn.CompletionRequired)
        {
            // the external cookie carries the login to the completion step, which takes it over
            return RedirectToPage("Register", "External", new { returnUrl });
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (login is null || user is null)
        {
            ErrorMessage = AuthTexts.T("The sign-in provider did not confirm the sign-in.");
            return Page();
        }

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
