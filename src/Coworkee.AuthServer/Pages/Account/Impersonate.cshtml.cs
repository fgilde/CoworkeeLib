using System.Security.Claims;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using static Coworkee.AuthServer.AuthTexts;

namespace Coworkee.AuthServer.Pages.Account;

/// <summary>
/// Redeems a ticket of <see cref="StartImpersonation"/>: signs the browser in as the target, with the impersonator in extra claims. No form to confirm,
/// following the link is the confirmation: the ticket lives a minute and works for the signed-in user who asked for it only.
/// </summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ImpersonateModel(
    ImpersonationTickets tickets, ImpersonationRules rules, SignInManager<User> signIn, UserManager<User> users, TimeProvider clock, ILogger<ImpersonateModel> logger)
    : PageModel
{
    public string? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? ticket, string? returnUrl)
    {
        var redeemed = tickets.Read(ticket);
        if (redeemed is null || !await AuthImpersonation.MayRedirectToAsync(HttpContext, Url, returnUrl))
        {
            return Fail(StatusCodes.Status400BadRequest, T("The link is invalid or has expired."));
        }

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var impersonator = await signIn.ValidateSecurityStampAsync(User);
        var current = Guid.TryParse(User.FindFirstValue(ImpersonationClaims.Impersonator), out var currentId) ? currentId : (Guid?)null;
        if (impersonator is null || impersonator.Id != redeemed.ImpersonatorId || await users.IsLockedOutAsync(impersonator)
            || await rules.CheckAsync(impersonator.Id, current, redeemed.TargetId, redeemed.TenantId, HttpContext.RequestAborted) is not null
            || await users.FindByIdAsync(redeemed.TargetId.ToString()) is not { } target)
        {
            return Fail(StatusCodes.Status403Forbidden, T("You may not sign in as this user."));
        }

        // the impersonation itself never outlives the browser; "Back to my account" restores a persistent own sign-in
        var persistent = (await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Properties?.IsPersistent == true;
        await signIn.SignInWithClaimsAsync(target, isPersistent: false, AuthImpersonation.StartClaims(impersonator, persistent, clock.GetUtcNow()));
        logger.Started(impersonator.Id, target.Id, redeemed.TenantId);
        return Redirect(returnUrl!);
    }

    private PageResult Fail(int status, string problem)
    {
        Problem = problem;
        Response.StatusCode = status;
        return Page();
    }
}
