using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

internal sealed class UpdateRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser, RoleOffer offer, Users.AdminReach reach) : IHandler<UpdateRole, Result>
{
    public async Task<Result> HandleAsync(UpdateRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && r.TenantId == currentUser.TenantId, cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        if (await reach.CheckAllAsync(cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var normalized = command.Role.Name.Trim().ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, role.Id, cancellationToken))
        {
            return RoleNames.Taken;
        }

        var offered = (!role.SelectableForRegistration && command.Role.SelectableForRegistration == true) || (!role.IsDefault && command.Role.IsDefault == true);
        if (offered && await offer.RefusedAsync([role.Id], null, cancellationToken) is { } refused)
        {
            return refused;
        }

        role.Name = command.Role.Name.Trim();
        role.NormalizedName = normalized;
        role.Description = command.Role.Description;
        role.SelectableForRegistration = command.Role.SelectableForRegistration ?? role.SelectableForRegistration;
        role.IsDefault = command.Role.IsDefault ?? role.IsDefault;
        return Result.Success();
    }
}
