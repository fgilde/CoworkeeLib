namespace Coworkee.Application.Authorization;

/// <param name="Granted">What the roles and grants already give the user, e.g. to grant also to holders of an HR permission.</param>
public sealed record ComputedPermissionContext(Guid UserId, Guid? TenantId, IReadOnlyCollection<string> Granted, IServiceProvider Services);
