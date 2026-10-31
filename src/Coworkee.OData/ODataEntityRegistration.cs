namespace Coworkee.OData;

public sealed record ODataEntityRegistration(Type EntityType, string EntitySet, string? Permission, IReadOnlyList<string> HiddenProperties)
{
    /// <summary>Properties only users with the permission see (property name to permission); for everyone else they are hidden.</summary>
    public IReadOnlyDictionary<string, string> ProtectedProperties { get; init; } = new Dictionary<string, string>();

    /// <summary>The key property of the set, <c>Id</c> unless <see cref="ODataEntityOptions{TEntity}.Key"/> named another.</summary>
    public string Key { get; init; } = "Id";

    /// <summary>The node limit of the set's queries (<see cref="ODataEntityOptions{TEntity}.MaxNodeCount"/>); null: <c>Coworkee:OData:MaxNodeCount</c>.</summary>
    public int? MaxNodeCount { get; init; }

    internal bool HasGuidId => Key == "Id" && EntityType.GetProperty("Id")?.PropertyType == typeof(Guid);

    internal Type KeyType => EntityType.GetProperty(Key)?.PropertyType ?? throw new InvalidOperationException($"{EntityType.Name} needs a {Key} property as the key of its OData set.");

    /// <summary>
    /// Hidden properties the EDM keeps: a base type other sets share declares them, so they cannot leave the model of this set alone.
    /// They are denied to everyone on this set, like a <see cref="ProtectedProperties"/> entry nobody holds the permission of.
    /// </summary>
    internal HashSet<string> SharedHidden { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal bool HasDenials => ProtectedProperties.Count > 0 || SharedHidden.Count > 0;
}
