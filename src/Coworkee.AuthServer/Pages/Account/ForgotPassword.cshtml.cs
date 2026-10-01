using Coworkee.Account;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class ForgotPasswordModel(CoworkeeDbContext db, IAccountMailer mailer) : PageModel
{
    [BindProperty]
    public EmailInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public async Task OnPostAsync()
    {
        var normalized = Input.Email.Trim().ToUpperInvariant();
        List<User> matches;
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, null)))
        {
            matches = await db.Set<User>().Where(u => u.NormalizedEmail == normalized && u.IsActive && u.EmailConfirmed).ToListAsync(HttpContext.RequestAborted);
        }

        foreach (var user in matches)
        {
            using (CurrentUserScope.Begin(new ImpersonatedUser(null, user.TenantId)))
            {
                await mailer.SendPasswordResetAsync(user, HttpContext.RequestAborted);
                await db.SaveChangesAsync(HttpContext.RequestAborted);
            }
        }

        Sent = true;
    }

    public sealed class EmailInput
    {
        public string Email { get; set; } = string.Empty;
    }
}
