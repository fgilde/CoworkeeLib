using Coworkee.Contracts.Settings;
using Coworkee.Domain;

namespace Coworkee.Settings;

public sealed class SettingValue : AuditedEntity
{
    public required string Name { get; set; }

    public SettingScope Scope { get; set; }

    public Guid? ScopeKey { get; set; }

    public string? Value { get; set; }
}
