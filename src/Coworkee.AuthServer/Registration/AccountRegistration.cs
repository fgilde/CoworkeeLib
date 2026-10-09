using Coworkee.Account;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Registration;

/// <summary>Creates self-registered accounts (wizard and external sign-in) in the unit of work of the caller, who saves.</summary>
public sealed class AccountRegistration(UserManager<User> users, CoworkeeDbContext db, IAccountMailer mailer, IServiceProvider services)
{
    public const string NotificationType = "account.registration";

    /// <summary>Runs as the new user (CurrentUserScope); activation and email confirmation follow <paramref name="policy"/>.</summary>
    public Task<IdentityResult> CreateAsync(User user, string? password, RegistrationPolicy policy)
    {
        user.IsActive = !policy.RequiresActivation;
        user.EmailConfirmed = user.EmailConfirmed || !policy.RequiresEmailConfirmation;
        return password is null ? users.CreateAsync(user) : users.CreateAsync(user, password);
    }

    /// <summary>Sends the confirmation or the pending mail and tells the administrators about accounts waiting for them.</summary>
    public async Task AnnounceAsync(User user, CancellationToken cancellationToken)
    {
        if (!user.EmailConfirmed)
        {
            await mailer.SendEmailConfirmationAsync(user, cancellationToken);
        }
        else if (!user.IsActive)
        {
            await mailer.SendRegistrationPendingAsync(user, cancellationToken);
        }

        if (!user.IsActive && services.GetService<INotifier>() is { } notifier)
        {
            var admins = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                                join role in db.Set<Role>() on userRole.RoleId equals role.Id
                                join admin in db.Set<User>() on userRole.UserId equals admin.Id
                                where role.IsSystem && role.Name == SystemRoles.Admin && admin.TenantId == user.TenantId && admin.IsActive
                                select admin.Id).ToListAsync(cancellationToken);
            var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
            await notifier.NotifyAsync(admins, NotificationType, "New registration", $"{(name.Length > 0 ? name : user.Email)} ({user.Email}) waits for activation.",
                $"/admin/users/{user.Id}", cancellationToken);
        }
    }
}
