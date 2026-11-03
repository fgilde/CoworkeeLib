namespace Coworkee.Infrastructure.Versioning;

public sealed record VersionedType(string Name, Type ClrType, string Permission);
