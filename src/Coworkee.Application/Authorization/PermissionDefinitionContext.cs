namespace Coworkee.Application.Authorization;

public sealed class PermissionDefinitionContext
{
    internal List<(string Name, string DisplayName)> Groups { get; } = [];

    internal List<PermissionDefinition> Definitions { get; } = [];

    public PermissionGroupBuilder Group(string name, string displayName)
    {
        // a module may add to a group of another module (the head script to Settings)
        if (!Groups.Exists(g => g.Name == name))
        {
            Groups.Add((name, displayName));
        }

        return new PermissionGroupBuilder(this, name);
    }
}
