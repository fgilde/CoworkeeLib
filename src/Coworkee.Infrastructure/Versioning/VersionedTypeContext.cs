using Coworkee.Domain;

namespace Coworkee.Infrastructure.Versioning;

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
