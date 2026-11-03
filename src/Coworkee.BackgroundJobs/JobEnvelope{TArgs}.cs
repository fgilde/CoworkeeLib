namespace Coworkee.BackgroundJobs;

/// <param name="ImpersonatorId">Who was signed in as <paramref name="UserId"/> when the job was enqueued ("Log in as"); the audit trail of the job names him too.</param>
public sealed record JobEnvelope<TArgs>(TArgs Args, Guid? UserId, Guid? TenantId, Guid? ImpersonatorId = null);
