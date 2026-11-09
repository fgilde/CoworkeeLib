using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Roles;

internal sealed class UpdateRoleHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, RoleOffer offer, Users.AdminReach reach, IOptions<Users.IdentityAdministrationOptions> options) : IHandler<UpdateRole, Result>
{
    public async Task<Result> HandleAsync(UpdateRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        // system roles are shared by every organisation and found by their name
        if (role.IsSystem)
        {
            return RoleNames.SystemRole;
        }

        if (await reach.CheckAllAsync(cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var name = command.Role.Name.Trim();
        if (name != role.Name && options.Value.FixedRoleNames.TryGetValue(role.Name!, out var usedBy))
        {
            return RoleNames.Fixed(usedBy, "renamed");
        }

        var normalized = name.ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, role.Id, cancellationToken))
        {
            return RoleNames.Taken;
        }

        var offered = (!role.SelectableForRegistration && command.Role.SelectableForRegistration == true) || (!role.IsDefault && command.Role.IsDefault == true);
        if (offered && await offer.RefusedAsync([role.Id], null, cancellationToken) is { } refused)
        {
            return refused;
        }

        role.Name = name;
        role.NormalizedName = normalized;
        role.Description = command.Role.Description;
        role.SelectableForRegistration = command.Role.SelectableForRegistration ?? role.SelectableForRegistration;
        role.IsDefault = command.Role.IsDefault ?? role.IsDefault;
        return Result.Success();
    }
}
