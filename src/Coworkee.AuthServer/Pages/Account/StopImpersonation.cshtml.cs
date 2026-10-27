using System.Security.Claims;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Coworkee.AuthServer.Pages.Account;

/// <summary>Ends "Log in as": revokes what the impersonation got and signs the impersonator in again.</summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class StopImpersonationModel(SignInManager<User> signIn, UserManager<User> users, ILogger<StopImpersonationModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ImpersonatorName { get; private set; }

    public string? UserName { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthImpersonation.MayRedirectToAsync(HttpContext, Url, ReturnUrl))
        {
            return BadRequest();
        }

        if (!AuthImpersonation.IsImpersonating(User))
        {
            return Redirect(ReturnUrl!);
        }

        ImpersonatorName = User.FindFirstValue(ImpersonationClaims.ImpersonatorName);
        UserName = User.Identity?.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AuthImpersonation.MayRedirectToAsync(HttpContext, Url, ReturnUrl))
        {
            return BadRequest();
        }

        if (!Guid.TryParse(User.FindFirstValue(ImpersonationClaims.Impersonator), out var impersonatorId))
        {
            return Redirect(ReturnUrl!);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var sid = AuthEndpoints.SessionOf(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme));
        var revoked = await AuthImpersonation.RevokeAsync(HttpContext.RequestServices, userId, sid, HttpContext.RequestAborted);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        if (await users.FindByIdAsync(impersonatorId.ToString()) is { IsActive: true } impersonator)
        {
            await signIn.SignInAsync(impersonator, isPersistent: false);
        }
        else
        {
            await signIn.SignOutAsync();
        }

        logger.Stopped(impersonatorId, Guid.Parse(userId), revoked);
        return Redirect(ReturnUrl!);
    }
}
