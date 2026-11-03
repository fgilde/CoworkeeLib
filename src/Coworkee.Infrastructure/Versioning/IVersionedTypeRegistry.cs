namespace Coworkee.Infrastructure.Versioning;

public interface IVersionedTypeRegistry
{
    VersionedType? Find(string name);
}
