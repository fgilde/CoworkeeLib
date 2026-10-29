using System.Security.Claims;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LogoutModel(SignInManager<User> signIn) : PageModel
{
    public async Task OnGetAsync()
    {
        // signing out while impersonating ends the impersonation's tokens too, like the end session endpoint
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (AuthImpersonation.IsImpersonating(cookie.Principal))
        {
            await AuthImpersonation.RevokeAsync(HttpContext.RequestServices, cookie.Principal!, cookie.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!, AuthEndpoints.SessionOf(cookie),
                HttpContext.RequestAborted);
        }

        await signIn.SignOutAsync();
    }
}
