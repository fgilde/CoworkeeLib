using Coworkee.AuthServer.External;
using Coworkee.Contracts.Configuration;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class ExternalLoginModel(
    SignInManager<User> signIn, ExternalSignIn external, CoworkeeDbContext db, ISettingProvider settings, ISettingDefinitionManager definitions,
    IOptions<AuthServerOptions> options) : PageModel
{
    public string? ErrorMessage { get; private set; }

    public string? ReturnUrl { get; private set; }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl, string? remoteError)
    {
        if (await LoginModes.ResolveAsync(settings, definitions, options.Value, HttpContext.RequestAborted) == LoginMode.Internal)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return NotFound();
        }

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
