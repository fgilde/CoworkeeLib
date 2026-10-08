using Coworkee.Domain;

namespace Coworkee.Localization.Domain;

/// <summary>A text an administrator set; it overrides the module resources of its culture.</summary>
public sealed class Translation : AuditedEntity
{
    public required string Culture { get; set; }

    public required string Key { get; set; }

    public required string Value { get; set; }
}
