using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Identity;

public static class IdentityPermissions
{
    public const string GroupName = "Identity";

    public static class Users
    {
        public const string View = "Identity.Users.View";
        public const string Manage = "Identity.Users.Manage";
    }

    public static class Groups
    {
        public const string View = "Identity.Groups.View";
        public const string Manage = "Identity.Groups.Manage";
    }

    public static class Roles
    {
        public const string View = "Identity.Roles.View";
        public const string Manage = "Identity.Roles.Manage";
    }

    public static class Permissions
    {
        public const string Manage = "Identity.Permissions.Manage";
    }

    public static class ResourcePermissions
    {
        public const string Manage = "Identity.ResourcePermissions.Manage";
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<PermissionProviderType>))]
public enum PermissionProviderType
{
    Role,
    User,
    Group,
}

[JsonConverter(typeof(JsonStringEnumConverter<PrincipalType>))]
public enum PrincipalType
{
    User,
    Group,
}
