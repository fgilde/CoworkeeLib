namespace Coworkee.Client.Blazor.Security;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string For(string permission) => Prefix + permission;

    /// <summary>A permission name that any of <paramref name="permissions"/> satisfies; usable wherever a permission name is (page policy, navigation item, <c>PermissionView</c>).</summary>
    public static string AnyOf(params string[] permissions) => string.Join('|', permissions);

    internal static string[] Split(string permission) => permission.Split('|');
}
