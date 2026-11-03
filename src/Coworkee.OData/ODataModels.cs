using System.Collections.Concurrent;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;

namespace Coworkee.OData;

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
