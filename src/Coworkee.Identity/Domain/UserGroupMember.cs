namespace Coworkee.Identity.Domain;

public sealed class UserGroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The member owns the group ("account owner"); ownership ends with the membership.</summary>
    public bool IsOwner { get; set; }
}
