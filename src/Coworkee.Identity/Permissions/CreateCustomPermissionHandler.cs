using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

internal sealed class CreateCustomPermissionHandler(CoworkeeDbContext db, ICurrentUser currentUser, ITenantDirectory tenants, IPermissionDefinitionManager definitions)
    : IHandler<CreateCustomPermission, Result>
{
    public async Task<Result> HandleAsync(CreateCustomPermission command, CancellationToken cancellationToken)
    {
        if (!await CustomPermissionRules.IsHostAsync(currentUser, tenants, cancellationToken))
        {
            return CustomPermissionRules.HostOnly;
        }

        var request = command.Permission;
        if (definitions.Exists(request.Name) || await db.Set<CustomPermission>().AnyAsync(p => p.Name == request.Name, cancellationToken))
        {
            return CustomPermissionRules.Taken;
        }

        db.Add(new CustomPermission
        {
            Name = request.Name,
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Group = CustomPermissionRules.GroupOf(request.Group),
        });
        return Result.Success();
    }
}
