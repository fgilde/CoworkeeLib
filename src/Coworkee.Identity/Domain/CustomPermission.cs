using Coworkee.Domain;

namespace Coworkee.Identity.Domain;

/// <summary>A permission an administrator created; defined for every organisation like the permissions in code, granted per organisation.</summary>
public sealed class CustomPermission : AuditedEntity
{
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public required string Group { get; set; }
}
