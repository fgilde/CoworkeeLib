namespace Coworkee.Application.Authorization;

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
