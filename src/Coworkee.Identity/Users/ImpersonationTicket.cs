namespace Coworkee.Identity.Users;

public sealed record ImpersonationTicket(Guid ImpersonatorId, Guid TargetId, Guid? TenantId);
