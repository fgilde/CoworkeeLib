using Coworkee.Domain;

namespace Coworkee.Infrastructure.Versioning;

public sealed record VersionedType(string Name, Type ClrType, string Permission);

public interface IVersionedTypeContributor
{
    void Define(VersionedTypeContext context);
}

public sealed class VersionedTypeContext
{
    internal List<VersionedType> Types { get; } = [];

    public VersionedTypeContext Add<T>(string name, string permission)
        where T : class, IVersioned
    {
        Types.Add(new VersionedType(name, typeof(T), permission));
        return this;
    }
}

public interface IVersionedTypeRegistry
{
    VersionedType? Find(string name);
}

internal sealed class VersionedTypeRegistry : IVersionedTypeRegistry
{
    private readonly Dictionary<string, VersionedType> _types;

    public VersionedTypeRegistry(IEnumerable<IVersionedTypeContributor> contributors)
    {
        var context = new VersionedTypeContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        _types = context.Types.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public VersionedType? Find(string name) => _types.GetValueOrDefault(name);
}
