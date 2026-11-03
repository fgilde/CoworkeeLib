using Coworkee.Domain;

namespace Coworkee.Mailing;

public sealed class MailTemplateOverride : AuditedAggregateRoot, IVersioned
{
    public required string Name { get; set; }

    public Guid? TenantId { get; set; }

    public required string Culture { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }

    public int Revision { get; set; }
}
