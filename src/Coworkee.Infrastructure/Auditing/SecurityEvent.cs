using Coworkee.Domain;
using Nextended.Core.Facets;

namespace Coworkee.Infrastructure.Auditing;

/// <summary>An entry of the security log (<c>ISecurityLog</c>); like the audit trail, written by every host with the app database.</summary>
[NotAudited]
public sealed class SecurityEvent
{
    public const int ActionLength = 64;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset At { get; init; }

    public Guid? TenantId { get; set; }

    /// <summary>Whose account the entry concerns.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Who did it, when that was someone else than <see cref="UserId"/> (an administrator).</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Who was signed in as the acting user ("Log in as").</summary>
    public Guid? ImpersonatorId { get; set; }

    [ProvideFacet(Label = "Action")]
    public required string Action { get; init; }

    [ProvideFacet(Label = "Succeeded")]
    public bool Succeeded { get; init; }

    [ProvideFacet(Label = "Reason")]
    public string? Reason { get; set; }

    public string? ClientId { get; init; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    public string? TraceId { get; init; }

    /// <summary>A JSON object.</summary>
    public string? Details { get; set; }
}
