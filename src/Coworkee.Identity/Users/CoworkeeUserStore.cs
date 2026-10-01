using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

// Saving happens in the unit of work, so one request can update a user several times (2FA, lockout, reset).
// The base store re-attaches on every update, which resets EF's original values and breaks the concurrency check.
internal sealed class CoworkeeUserStore(CoworkeeDbContext context) : UserStore<User, Role, CoworkeeDbContext, Guid>(context)
{
    public override async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (Context.Entry(user).State == EntityState.Detached)
        {
            return await base.UpdateAsync(user, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        return IdentityResult.Success;
    }
}
