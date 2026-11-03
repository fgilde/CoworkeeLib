namespace Coworkee.Domain;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RealtimeAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;

    public string? ResourceType { get; init; }
}
