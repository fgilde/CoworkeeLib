using Coworkee.Application.Authorization;
using Coworkee.Contracts.Workflows;
using Coworkee.Realtime;

namespace Coworkee.Workflows;

/// <summary>Lets people who may see workflows follow "workflow:{entityType}:{entityId}" of entities of their organisation.</summary>
internal sealed class WorkflowTopicAuthorizer(WorkflowEngine engine, IPermissionChecker permissions) : IRealtimeTopicAuthorizer
{
    private const string Prefix = "workflow:";

    public bool Handles(string topic) => topic.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken)
    {
        var separator = topic.LastIndexOf(':');
        return separator > Prefix.Length
            && Guid.TryParse(topic[(separator + 1)..], out var entityId)
            && WorkflowTopics.Entity(topic[Prefix.Length..separator], entityId) == topic
            && await permissions.IsGrantedAsync(WorkflowPermissions.View, cancellationToken)
            && (await engine.LoadAsync(topic[Prefix.Length..separator], entityId, cancellationToken)).IsSuccess;
    }
}
