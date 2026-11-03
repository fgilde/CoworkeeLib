namespace Coworkee.Infrastructure.Tokens;

/// <summary>What a redeemed token opens, with the organisation and user that issued it.</summary>
public sealed record AccessTokenGrant(Guid TargetId, Guid? TenantId, Guid? UserId);
