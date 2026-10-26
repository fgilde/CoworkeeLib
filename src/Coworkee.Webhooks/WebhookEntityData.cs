using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Coworkee.Application.Groups;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.ResponseFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Webhooks;

/// <summary>An entity as the current user reads it through its OData set: permission, row filters, hidden properties and response filters apply.</summary>
internal static class WebhookEntityData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    private static readonly MethodInfo LoadMethod = typeof(WebhookEntityData).GetMethod(nameof(LoadAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The data to send, or null when the user may not see the entity; a hard deleted entity is only its id.</summary>
    public static async Task<JsonNode?> ReadAsync(IServiceProvider services, ODataEntityRegistration set, string key, string action, CancellationToken cancellationToken)
    {
        if (!await ODataEntityAccess.IsGrantedAsync(services, set.EntityType, cancellationToken))
        {
            return null;
        }

        var entity = await (Task<object?>)LoadMethod.MakeGenericMethod(set.EntityType).Invoke(null, [services, key, cancellationToken])!;
        if (entity is null)
        {
            // ponytail: a hard delete leaves no row to check row filters against; the id alone goes to everyone with the set's permission
            return action == WebhookNames.Deleted ? new JsonObject { ["id"] = key } : null;
        }

        var shaped = await ResponseFilterApplier.ApplyAsync(services, entity, Json, cancellationToken);
        var node = shaped as JsonNode ?? JsonSerializer.SerializeToNode(shaped, shaped!.GetType(), Json);
        return node is JsonObject data ? Keep(data, Columns(services, set, await ODataEntityAccess.DeniedPropertiesAsync(services, set.EntityType, cancellationToken))) : node;
    }

    private static async Task<object?> LoadAsync<TEntity>(IServiceProvider services, string key, CancellationToken cancellationToken)
        where TEntity : class
    {
        using var unnarrowed = GroupScope.Unnarrowed();
        var visible = await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, withDeleted: true);
        var id = typeof(TEntity).GetProperty("Id")!;
        var entity = Expression.Parameter(typeof(TEntity));
        var match = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(Expression.Property(entity, id), Expression.Constant(TypeDescriptor.GetConverter(id.PropertyType).ConvertFromInvariantString(key), id.PropertyType)), entity);
        return await visible.FirstOrDefaultAsync(match, cancellationToken);
    }

    // only the columns of the set: navigations are not loaded, hidden properties and protected ones the owner may not read never leave
    private static HashSet<string> Columns(IServiceProvider services, ODataEntityRegistration set, IReadOnlySet<string> denied) =>
    [
        .. services.GetRequiredService<CoworkeeDbContext>().Model.FindEntityType(set.EntityType)!.GetProperties()
            .Where(p => !p.IsShadowProperty() && !set.HiddenProperties.Contains(p.Name) && !denied.Contains(p.Name))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)),
    ];

    private static JsonObject Keep(JsonObject data, HashSet<string> columns)
    {
        foreach (var name in data.Select(p => p.Key).Where(name => !columns.Contains(name)).ToList())
        {
            data.Remove(name);
        }

        return data;
    }
}
