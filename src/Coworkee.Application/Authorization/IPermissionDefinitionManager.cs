namespace Coworkee.Application.Authorization;

public interface IPermissionDefinitionManager
{
    IReadOnlyList<PermissionDefinition> All { get; }

    IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    bool Exists(string name);

    IReadOnlySet<string> Expand(IEnumerable<string> granted);
}
