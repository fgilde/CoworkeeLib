using Coworkee.Domain;

namespace Coworkee.Mailing;

public sealed class RecipientList : Entity
{
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public required string Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
