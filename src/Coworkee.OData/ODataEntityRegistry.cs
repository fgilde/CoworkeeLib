namespace Coworkee.OData;

public sealed class ODataEntityRegistry
{
    private readonly List<ODataEntityRegistration> _entities = [];

    public IReadOnlyList<ODataEntityRegistration> Entities => _entities;

    public ODataEntityRegistration? Find(Type entityType) => _entities.FirstOrDefault(e => e.EntityType == entityType);

    internal void Add(ODataEntityRegistration registration)
    {
        if (_entities.Any(e => e.EntityType == registration.EntityType || string.Equals(e.EntitySet, registration.EntitySet, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{registration.EntityType.Name} or the entity set {registration.EntitySet} is registered twice.");
        }

        _entities.Add(registration);
    }
}
