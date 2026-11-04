namespace Coworkee.Application.Authorization;

internal sealed class PermissionDefinitionManager(IEnumerable<IPermissionDefinitionContributor> contributors, IEnumerable<IStoredPermissionDefinitions> stored)
    : IPermissionDefinitionManager
{
    private readonly Lazy<PermissionDefinitionContext> _code = new(() => Define(contributors));
    private readonly IStoredPermissionDefinitions[] _stored = [.. stored];
    private Model? _model;

    public IReadOnlyList<PermissionDefinition> All => Current.All;

    public IReadOnlyList<(string Name, string DisplayName)> Groups => Current.Groups;

    public bool Exists(string name) => Current.ByName.ContainsKey(name);

    public IReadOnlySet<string> Expand(IEnumerable<string> granted)
    {
        var byName = Current.ByName;
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(granted.Where(byName.ContainsKey));
        while (pending.TryPop(out var name))
        {
            if (result.Add(name))
            {
                foreach (var implied in byName[name].Implies.Where(byName.ContainsKey))
                {
                    pending.Push(implied);
                }
            }
        }

        return result;
    }

    // the stores hand out the same lists until their definitions change, so comparing references tells when to rebuild
    private Model Current
    {
        get
        {
            var lists = Array.ConvertAll(_stored, s => s.Current);
            var model = _model;
            if (model is null || !model.Sources.AsSpan().SequenceEqual(lists, ReferenceEqualityComparer.Instance))
            {
                _model = model = Build(_code.Value, lists);
            }

            return model;
        }
    }

    private static PermissionDefinitionContext Define(IEnumerable<IPermissionDefinitionContributor> contributors)
    {
        var context = new PermissionDefinitionContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in context.Definitions.Where(d => !names.Add(d.Name)))
        {
            throw new InvalidOperationException($"Permission '{definition.Name}' is defined twice.");
        }

        var unknown = context.Definitions.SelectMany(d => d.Implies.Where(i => !names.Contains(i)).Select(i => $"{d.Name} -> {i}")).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException($"Unknown implied permissions: {string.Join(", ", unknown)}");
        }

        return context;
    }

    private static Model Build(PermissionDefinitionContext code, IReadOnlyList<PermissionDefinition>[] sources)
    {
        var byName = code.Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);
        var all = new List<PermissionDefinition>(code.Definitions);
        var groups = new List<(string Name, string DisplayName)>(code.Groups);
        foreach (var definition in sources.SelectMany(s => s).Where(d => byName.TryAdd(d.Name, d)))
        {
            all.Add(definition);
            if (!groups.Exists(g => g.Name == definition.Group))
            {
                groups.Add((definition.Group, definition.Group));
            }
        }

        return new Model(sources, all, groups, byName);
    }

    private sealed record Model(
        IReadOnlyList<PermissionDefinition>[] Sources, IReadOnlyList<PermissionDefinition> All, IReadOnlyList<(string Name, string DisplayName)> Groups,
        Dictionary<string, PermissionDefinition> ByName);
}
