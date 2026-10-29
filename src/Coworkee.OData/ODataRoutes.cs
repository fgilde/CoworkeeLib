using System.Collections.Concurrent;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;

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

/// <summary>The EDM model of each route, built once from the same registry: the routes differ in names only.</summary>
internal sealed class ODataModels(
    IServiceProvider services, ODataRoutes routes, ODataEntityRegistry registry, Computed.ODataComputedProperties computed, IOptions<CoworkeeODataOptions> options)
{
    private readonly ConcurrentDictionary<string, Lazy<IEdmModel>> _models = new(StringComparer.OrdinalIgnoreCase);

    public IEdmModel Of(ODataRouteProfile profile)
    {
        // the default model finds the hidden properties a shared base keeps (SharedHidden), which every route denies
        if (profile != routes.Default)
        {
            _ = Of(routes.Default);
        }

        return _models.GetOrAdd(profile.Prefix, _ => new Lazy<IEdmModel>(() => Build(profile))).Value;
    }

    /// <summary>The model of the route with the prefix; the default model for null.</summary>
    public IEdmModel Of(string? prefix) =>
        Of(prefix is null ? routes.Default : routes.Find(prefix) ?? throw new InvalidOperationException($"There is no OData route {prefix}."));

    private IEdmModel Build(ODataRouteProfile profile)
    {
        var isDefault = profile == routes.Default;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<CoworkeeODataModule>();
        if (!options.Value.ModelFromEntityFramework)
        {
            return ODataModel.Build(profile, registry, computed, null, isDefault, logger);
        }

        using var scope = services.CreateScope();
        return ODataModel.Build(profile, registry, computed, type => scope.ServiceProvider.DbContextFor(type).Model.FindEntityType(type), isDefault, logger);
    }
}
