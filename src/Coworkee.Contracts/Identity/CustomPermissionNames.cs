namespace Coworkee.Contracts.Identity;

/// <summary>Custom permissions are the ones administrators create at runtime; the prefix keeps them apart from every permission in code.</summary>
public static class CustomPermissionNames
{
    public const string Prefix = "Custom.";

    /// <summary>The group of a custom permission that names none.</summary>
    public const string DefaultGroup = "Custom";
}
