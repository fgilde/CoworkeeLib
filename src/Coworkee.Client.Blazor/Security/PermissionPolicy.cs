namespace Coworkee.Client.Blazor.Security;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string For(string permission) => Prefix + permission;
}
