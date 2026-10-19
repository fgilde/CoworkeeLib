namespace Coworkee.OData;

public sealed record ODataEntityRegistration(Type EntityType, string EntitySet, string? Permission, IReadOnlyList<string> HiddenProperties)
{
    /// <summary>Properties only users with the permission see (property name to permission); for everyone else they are hidden.</summary>
    public IReadOnlyDictionary<string, string> ProtectedProperties { get; init; } = new Dictionary<string, string>();
}
