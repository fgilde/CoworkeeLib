namespace Coworkee.OData;

/// <summary>The OData routes of the app: <c>/odata</c> and the ones <see cref="ODataServiceCollectionExtensions.AddCoworkeeODataRoute"/> added.</summary>
public sealed class ODataRoutes
{
    private readonly List<ODataRouteProfile> _profiles = [new(CoworkeeODataModule.RoutePrefix)];

    public IReadOnlyList<ODataRouteProfile> Profiles => _profiles;

    /// <summary>The route <c>/odata</c> with the CLR names.</summary>
    public ODataRouteProfile Default => _profiles[0];

    /// <summary>The route with the prefix (<c>rt/odata</c>, leading and trailing slashes ignored), null when there is none.</summary>
    public ODataRouteProfile? Find(string? prefix) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Prefix, prefix?.Trim('/'), StringComparison.OrdinalIgnoreCase));

    internal ODataRouteProfile Add(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix.Trim('/')))
        {
            throw new ArgumentException("An OData route needs a prefix, e.g. rt/odata.", nameof(prefix));
        }

        if (Find(prefix) is not null)
        {
            throw new InvalidOperationException($"The OData route {prefix} is added twice.");
        }

        var profile = new ODataRouteProfile(prefix);
        _profiles.Add(profile);
        return profile;
    }
}
