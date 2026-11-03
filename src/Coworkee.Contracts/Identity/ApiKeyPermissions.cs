namespace Coworkee.Contracts.Identity;

public static class ApiKeyPermissions
{
    public const string GroupName = "ApiKeys";

    /// <summary>Create and revoke own keys, limited to the permissions the user has.</summary>
    public const string Create = "ApiKeys.Create";

    /// <summary>See and revoke every key of the organisation.</summary>
    public const string Manage = "ApiKeys.Manage";
}
