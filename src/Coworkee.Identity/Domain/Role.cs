using Coworkee.Contracts.Identity;
using Coworkee.Domain;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Domain;

[Realtime(IdentityPermissions.Roles.View)]
public sealed class Role : IdentityRole<Guid>, IAuditable
{
    public Role() => Id = Guid.CreateVersion7();

    public Guid? TenantId { get; set; }

    public string? Description { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "System")]
    public bool IsSystem { get; set; }

    /// <summary>Offered in the registration wizard; new users pick it themselves.</summary>
    public bool SelectableForRegistration { get; set; }

    /// <summary>Every new user gets the role, however the user is created; never set on system roles.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
