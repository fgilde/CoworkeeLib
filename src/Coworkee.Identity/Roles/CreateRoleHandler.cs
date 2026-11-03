using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Identity.Roles;

internal sealed class CreateRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser, RoleOffer offer, Users.AdminReach reach) : IHandler<CreateRole, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRole command, CancellationToken cancellationToken)
    {
        // roles are the organisation's: a group-scoped administrator uses them, he does not shape them
        if (await reach.CheckAllAsync(cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var normalized = command.Role.Name.Trim().ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, null, cancellationToken))
        {
            return RoleNames.Taken;
        }

        if ((command.Role.SelectableForRegistration == true || command.Role.IsDefault == true) && await offer.RefusedAsync([], null, cancellationToken) is { } refused)
        {
            return refused;
        }

        var role = new Role
        {
            Name = command.Role.Name.Trim(), NormalizedName = normalized, Description = command.Role.Description, TenantId = currentUser.TenantId,
            SelectableForRegistration = command.Role.SelectableForRegistration ?? false, IsDefault = command.Role.IsDefault ?? false,
        };
        db.Add(role);
        return role.Id;
    }
}
