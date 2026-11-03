namespace Coworkee.Application.Authorization;

/// <summary>On a command or query, or on an application service class or method: the permissions the caller needs.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
