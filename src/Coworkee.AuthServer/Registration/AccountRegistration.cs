using Coworkee.Account;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Security;
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

    /// <summary>The roles a new user may pick, of the tenant and global ones; never system roles.</summary>
    public async Task<IReadOnlyList<Role>> SelectableRolesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var anyTenant = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        return await db.Set<Role>().AsNoTracking()
            .Where(r => r.SelectableForRegistration && !r.IsSystem && (r.TenantId == null || r.TenantId == tenantId))
            .OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    /// <summary>Whether an external sign-up lacks what the registration asks for and goes through the completion step first.</summary>
    public static bool NeedsCompletion(RegistrationOptions options, bool hasRoles, string? firstName, string? lastName) =>
        options.RequireAddress || hasRoles || (options.RequireDocuments && options.Documents.Count > 0)
        || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName);

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
            await notifier.NotifyLocalizedAsync(admins, NotificationType, "New registration", "{0} ({1}) waits for activation.",
                [name.Length > 0 ? name : user.Email!, user.Email!], $"/admin/users/{user.Id}", cancellationToken);
        }
    }
}
