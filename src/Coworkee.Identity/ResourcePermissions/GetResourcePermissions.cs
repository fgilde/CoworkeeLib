using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.ResourcePermissions;

public sealed record GetResourcePermissions(string ResourceType, Guid ResourceId)
    : ResourcePermissionRequest(ResourceType, ResourceId), IQuery<Result<IReadOnlyList<ResourcePermissionDto>>>;
