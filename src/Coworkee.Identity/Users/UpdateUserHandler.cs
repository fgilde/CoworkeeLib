using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class UpdateUserHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, UserManager<User> users, Profile.UserChanges changes, IEnumerable<IUserActivationListener> activation,
    AccountGuard guard)
    : IHandler<UpdateUser, Result>
{
    public async Task<Result> HandleAsync(UpdateUser command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (await guard.CheckUserAsync(user.Id, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (user.IsActive && !command.User.IsActive && await AdminGuard.IsAdminAsync(db, user.Id, cancellationToken)
            && !await AdminGuard.OtherActiveAdminExistsAsync(db, currentUser.TenantId, user.Id, cancellationToken))
        {
            return AdminGuard.LastAdmin;
        }

        var request = command.User;
        if (request.UserName?.Trim() is { } userName && userName != user.UserName && await users.SetUserNameAsync(user, userName) is { Succeeded: false } renamed)
        {
            return IdentityErrors.ToError(renamed);
        }

        var activated = !user.IsActive && request.IsActive;
        if (user.IsActive && !request.IsActive)
        {
            // ends the user's sessions, so a deactivated user is signed out at once
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.IsActive = request.IsActive;
        user.MustChangePassword = request.MustChangePassword ?? user.MustChangePassword;
        user.EmailConfirmed = request.EmailConfirmed ?? user.EmailConfirmed;
        user.PhoneNumber = request.PhoneNumber is null ? user.PhoneNumber : Clean(request.PhoneNumber);
        if (request.Address is { } address)
        {
            (user.Street, user.ZipCode, user.City, user.Country) = (Clean(address.Street), Clean(address.ZipCode), Clean(address.City), Clean(address.Country));
        }

        await db.SaveChangesAsync(cancellationToken);
        await changes.NotifyAsync(user, cancellationToken);
        if (activated)
        {
            foreach (var listener in activation)
            {
                await listener.UserActivatedAsync(user, cancellationToken);
            }
        }

        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
