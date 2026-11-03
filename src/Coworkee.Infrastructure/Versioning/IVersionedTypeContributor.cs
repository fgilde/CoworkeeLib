namespace Coworkee.Infrastructure.Versioning;

public interface IVersionedTypeContributor
{
    void Define(VersionedTypeContext context);
}
