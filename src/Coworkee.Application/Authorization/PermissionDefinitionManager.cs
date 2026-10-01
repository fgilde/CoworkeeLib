namespace Coworkee.Application.Authorization;

internal sealed class PermissionDefinitionManager(IEnumerable<IPermissionDefinitionContributor> contributors) : IPermissionDefinitionManager
{
    private readonly Lazy<(IReadOnlyList<PermissionDefinition> All, IReadOnlyList<(string, string)> Groups, Dictionary<string, PermissionDefinition> ByName)> _model =
        new(() => Build(contributors));

    public IReadOnlyList<PermissionDefinition> All => _model.Value.All;

    public IReadOnlyList<(string Name, string DisplayName)> Groups => _model.Value.Groups;

    public bool Exists(string name) => _model.Value.ByName.ContainsKey(name);

    public IReadOnlySet<string> Expand(IEnumerable<string> granted)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(granted.Where(Exists));
        while (pending.TryPop(out var name))
        {
            if (result.Add(name))
            {
                foreach (var implied in _model.Value.ByName[name].Implies)
                {
                    pending.Push(implied);
                }
            }
        }

        return result;
    }

    private static (IReadOnlyList<PermissionDefinition>, IReadOnlyList<(string, string)>, Dictionary<string, PermissionDefinition>) Build(
        IEnumerable<IPermissionDefinitionContributor> contributors)
    {
        var context = new PermissionDefinitionContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        var byName = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var definition in context.Definitions)
        {
            if (!byName.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException($"Permission '{definition.Name}' is defined twice.");
            }
        }

        var unknown = context.Definitions.SelectMany(d => d.Implies.Where(i => !byName.ContainsKey(i)).Select(i => $"{d.Name} -> {i}")).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException($"Unknown implied permissions: {string.Join(", ", unknown)}");
        }

        return (context.Definitions, context.Groups, byName);
    }
}
