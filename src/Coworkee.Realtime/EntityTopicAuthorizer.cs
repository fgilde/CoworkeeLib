using Coworkee.Application.Authorization;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Realtime;

internal sealed class EntityTopicAuthorizer(CoworkeeDbContext db, IPermissionChecker permissions) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith("type:", StringComparison.Ordinal) || topic.StartsWith("entity:", StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken)
    {
        var parts = topic.Split(':');
        var valid = parts is ["type", _] || (parts is ["entity", _, _] && parts[2].Length > 0);
        var attribute = valid
            ? db.Model.GetEntityTypes().Where(t => t.BaseType is null).Select(t => t.ClrType).FirstOrDefault(t => t.Name == parts[1])?
                .GetCustomAttributes(typeof(RealtimeAttribute), false).OfType<RealtimeAttribute>().FirstOrDefault()
            : null;
        if (attribute is null)
        {
            return false;
        }

        if (await permissions.IsGrantedAsync(attribute.Permission, cancellationToken))
        {
            return true;
        }

        return parts is ["entity", _, _] && attribute.ResourceType is { } resourceType && Guid.TryParse(parts[2], out var resourceId)
            && await permissions.IsGrantedAsync(attribute.Permission, resourceType, resourceId, cancellationToken);
    }
}
