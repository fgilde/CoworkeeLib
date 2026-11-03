using Coworkee.Domain;

namespace Coworkee.OData.ClientEntities;

/// <summary>A row of <c>cw.ClientEntityTouches</c>: <see cref="EntityId"/> of <see cref="EntityType"/> changed at <see cref="TouchedAt"/>.</summary>
[NotAudited]
public sealed class ClientEntityTouch : Entity
{
    /// <summary>The tenant of the caller who touched it; the feed reads its own and those without one, then keeps only the rows the reader sees.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The full name of the entity's CLR type.</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public DateTimeOffset TouchedAt { get; set; }
}
