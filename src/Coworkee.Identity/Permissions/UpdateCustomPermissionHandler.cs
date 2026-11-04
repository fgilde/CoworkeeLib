using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Identity.Permissions;

internal sealed class UpdateCustomPermissionHandler(
    CoworkeeDbContext db, ICurrentUser currentUser, ITenantDirectory tenants, IPermissionDefinitionManager definitions, CustomPermissionDefinitions stored)
    : IHandler<UpdateCustomPermission, Result>
{
    public async Task<Result> HandleAsync(UpdateCustomPermission command, CancellationToken cancellationToken)
    {
        var found = await CustomPermissionRules.FindAsync(db, currentUser, tenants, definitions, stored, command.Name, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var request = command.Permission;
        found.Value.DisplayName = request.DisplayName.Trim();
        found.Value.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        found.Value.Group = CustomPermissionRules.GroupOf(request.Group);
        return Result.Success();
    }
}
