using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.ResourcePermissions;

public sealed record GrantResourcePermission(string ResourceType, Guid ResourceId, GrantResourcePermissionRequest Grant)
    : ResourcePermissionRequest(ResourceType, ResourceId), ICommand<Result<Guid>>;
