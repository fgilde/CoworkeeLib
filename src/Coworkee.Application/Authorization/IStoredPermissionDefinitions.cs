namespace Coworkee.Application.Authorization;

/// <summary>
/// Permissions defined at runtime, such as the custom permissions administrators create. <see cref="IPermissionDefinitionManager"/> reads
/// <see cref="Current"/> on every use and rebuilds only when it gets another instance, so return the same list until the definitions change.
/// A stored name a contributor defines in code is ignored.
/// </summary>
public interface IStoredPermissionDefinitions
{
    IReadOnlyList<PermissionDefinition> Current { get; }
}
