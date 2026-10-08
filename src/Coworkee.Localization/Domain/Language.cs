using Coworkee.Domain;

namespace Coworkee.Localization.Domain;

public sealed class Language : AuditedEntity
{
    public required string Culture { get; set; }

    public required string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsDefault { get; set; }
}
