namespace Coworkee.Domain;

public abstract class AuditedEntity : Entity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
