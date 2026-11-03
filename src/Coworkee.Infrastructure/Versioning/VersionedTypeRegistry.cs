namespace Coworkee.Infrastructure.Versioning;

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
