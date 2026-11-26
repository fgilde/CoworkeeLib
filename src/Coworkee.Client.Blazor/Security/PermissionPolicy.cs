namespace Coworkee.Client.Blazor.Security;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string For(string permission) => Prefix + permission;

    /// <summary>
    /// A permission name that any of <paramref name="permissions"/> satisfies, for what checks it through the policy (<see cref="For"/>): page policy,
    /// navigation item, <c>PermissionView</c>. <see cref="PermissionStore.HasAsync(string)"/> does not split it: it looks for the joined name.
    /// </summary>
    public static string AnyOf(params string[] permissions) => string.Join('|', permissions);

    internal static string[] Split(string permission) => permission.Split('|');
}
