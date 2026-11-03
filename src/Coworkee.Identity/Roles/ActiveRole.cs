namespace Coworkee.Identity.Roles;

/// <summary>The chosen role is a user claim, so it follows the user to every device and needs no column of its own.</summary>
public static class ActiveRole
{
    public const string ClaimType = "coworkee:active_role";
}
