using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.ResourcePermissions;

public sealed record RevokeResourcePermission(string ResourceType, Guid ResourceId, Guid Id)
    : ResourcePermissionRequest(ResourceType, ResourceId), ICommand<Result>;
