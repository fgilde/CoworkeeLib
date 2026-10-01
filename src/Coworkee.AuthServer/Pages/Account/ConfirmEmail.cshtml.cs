using Coworkee.Account;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class ConfirmEmailModel(UserManager<User> users, IAccountMailer mailer, CoworkeeDbContext db) : PageModel
{
    public bool Confirmed { get; private set; }

    public bool PendingApproval { get; private set; }

    public async Task OnGetAsync(Guid userId, string? code)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByIdAsync(userId.ToString());
        var token = AccountTokens.Decode(code);
        if (user is null || token is null)
        {
            return;
        }

        var alreadyConfirmed = user.EmailConfirmed;
        if (!alreadyConfirmed && !(await users.ConfirmEmailAsync(user, token)).Succeeded)
        {
            return;
        }

        Confirmed = true;
        PendingApproval = !user.IsActive;
        if (PendingApproval && !alreadyConfirmed)
        {
            using (CurrentUserScope.Begin(new ImpersonatedUser(null, user.TenantId)))
            {
                await mailer.SendRegistrationPendingAsync(user, HttpContext.RequestAborted);
            }
        }

        await db.SaveChangesAsync(HttpContext.RequestAborted);
    }
}
