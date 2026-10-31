namespace Coworkee.Application.Authorization;

public sealed record PermissionDefinition(string Name, string Group, string DisplayName, IReadOnlyList<string> Implies);

public interface IPermissionDefinitionContributor
{
    void Define(PermissionDefinitionContext context);
}

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

public sealed class PermissionGroupBuilder
{
    private readonly PermissionDefinitionContext _context;
    private readonly string _group;

    internal PermissionGroupBuilder(PermissionDefinitionContext context, string group)
    {
        _context = context;
        _group = group;
    }

    public PermissionGroupBuilder Add(string name, string displayName, params string[] implies)
    {
        _context.Definitions.Add(new PermissionDefinition(name, _group, displayName, implies));
        return this;
    }
}

public interface IPermissionDefinitionManager
{
    IReadOnlyList<PermissionDefinition> All { get; }

    IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    bool Exists(string name);

    IReadOnlySet<string> Expand(IEnumerable<string> granted);
}
