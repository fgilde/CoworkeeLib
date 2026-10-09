using Coworkee.Account;
using Coworkee.Account.Email;
using Coworkee.AuthServer.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class ConfirmEmailChangeModel(UserManager<User> users, CoworkeeDbContext db, IOptions<AuthServerOptions> options) : PageModel
{
    public bool Changed { get; private set; }

    public string? Email { get; private set; }

    public async Task OnGetAsync(Guid userId, string? email, string? code)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByIdAsync(userId.ToString());
        var token = AccountTokens.Decode(code);
        if (user is null || token is null || string.IsNullOrWhiteSpace(email) || !Wildcards.Allows(options.Value.Login.AllowedEmails, email))
        {
            return;
        }

        using (CurrentUserScope.Begin(new ImpersonatedUser(user.Id, user.TenantId)))
        {
            if (await EmailChange.ApplyAsync(users, user, () => users.ChangeEmailAsync(user, email, token)) is not null)
            {
                return;
            }

            await db.SaveChangesAsync(HttpContext.RequestAborted);
        }

        (Changed, Email) = (true, email);
    }
}
