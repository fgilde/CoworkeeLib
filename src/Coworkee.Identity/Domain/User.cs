using Coworkee.Contracts.Identity;
using Coworkee.Domain;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Domain;

[Realtime(IdentityPermissions.Users.View)]
public sealed class User : IdentityUser<Guid>, IAuditable
{
    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Guid TenantId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    [Nextended.Core.Facets.ProvideFacet(Label = "Active")]
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>The next sign-in asks for a new password before the app gets tokens.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>When the password was set last; password expiry counts from here.</summary>
    public DateTimeOffset? PasswordChangedAt { get; set; }

    /// <summary>Hashes of earlier passwords, newest first, one per line; the password history setting refuses them.</summary>
    public string? PasswordHistory { get; set; }

    /// <summary>The session that changed its own password keeps its tokens until their next refresh (<see cref="Users.SessionStamp.Keep"/>).</summary>
    public string? KeptSession { get; set; }

    /// <summary>The sessions the user may have at once: null takes the organisation's setting, 0 is unlimited.</summary>
    public int? MaxSessions { get; set; }

    /// <summary>Profile picture as data URL.</summary>
    public string? AvatarUrl { get; set; }

    public DateTimeOffset? AvatarChangedAt { get; set; }

    /// <summary>The groups the user is a member of; lets OData filter users by group (<c>Groups/any(m: m/GroupId eq …)</c>).</summary>
    public List<UserGroupMember> Groups { get; } = [];

    public string? Street { get; set; }

    public string? ZipCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
