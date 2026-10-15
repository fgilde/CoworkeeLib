namespace Coworkee.OData;

public sealed class ODataEntityRegistry
{
    private readonly List<ODataEntityRegistration> _entities = [];
    private readonly List<Transfer.ODataImportRegistration> _imports = [];
    private readonly Dictionary<(Type, string), Contracts.Data.FacetBehaviorAttribute> _facets = [];

    public IReadOnlyList<ODataEntityRegistration> Entities => _entities;

    public IReadOnlyList<Transfer.ODataImportRegistration> Imports => _imports;

    public ODataEntityRegistration? Find(string entitySet) =>
        _entities.FirstOrDefault(e => string.Equals(e.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));

    public ODataEntityRegistration? Find(Type entityType) => _entities.FirstOrDefault(e => e.EntityType == entityType);

    public Transfer.ODataImportRegistration? FindImport(string entitySet) =>
        _imports.FirstOrDefault(i => string.Equals(i.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));

    /// <summary>The behavior of the facet on a property: configured with ConfigureFacet, else its [FacetBehavior].</summary>
    public Contracts.Data.FacetBehaviorAttribute? FacetBehavior(Type entityType, System.Reflection.PropertyInfo property) =>
        _facets.TryGetValue((entityType, property.Name), out var behavior) ? behavior
            : System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Contracts.Data.FacetBehaviorAttribute>(property);

    internal void Add(Transfer.ODataImportRegistration import) => _imports.Add(import);

    internal void Configure(Type entityType, string property, Contracts.Data.FacetBehaviorAttribute behavior) => _facets[(entityType, property)] = behavior;

    internal void Add(ODataEntityRegistration registration)
    {
        if (_entities.Any(e => e.EntityType == registration.EntityType || string.Equals(e.EntitySet, registration.EntitySet, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{registration.EntityType.Name} or the entity set {registration.EntitySet} is registered twice.");
        }

        _entities.Add(registration);
    }
}
