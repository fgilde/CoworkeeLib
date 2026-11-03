using Coworkee.Domain;

namespace Coworkee.Mailing;

public sealed class CustomMailTemplate : AuditedEntity
{
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }
}
