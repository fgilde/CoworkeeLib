using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Permissions;

internal sealed class GetPermissionDefinitionsHandler(IPermissionDefinitionManager definitions) : IHandler<GetPermissionDefinitions, Result<IReadOnlyList<PermissionGroupDto>>>
{
    public Task<Result<IReadOnlyList<PermissionGroupDto>>> HandleAsync(GetPermissionDefinitions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionGroupDto> groups = definitions.Groups
            .Select(g => new PermissionGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name)
                .Select(d => new PermissionDto(d.Name, d.DisplayName, d.Implies))
                .ToList()))
            .ToList();
        return Task.FromResult<Result<IReadOnlyList<PermissionGroupDto>>>(Result<IReadOnlyList<PermissionGroupDto>>.Success(groups));
    }
}
