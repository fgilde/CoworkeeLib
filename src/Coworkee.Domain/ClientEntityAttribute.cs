namespace Coworkee.Domain;

/// <summary>
/// Clients may mirror the entity's OData set and query it locally once it arrived (Coworkee.OData sync feed).
/// <see cref="MaxRows"/> above zero overrides the default limit beyond which clients keep asking the server; <see cref="Expand"/> names the
/// navigation properties the copy carries (as in $expand).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ClientEntityAttribute : Attribute
{
    public int MaxRows { get; init; }

    public string? Expand { get; init; }
}
