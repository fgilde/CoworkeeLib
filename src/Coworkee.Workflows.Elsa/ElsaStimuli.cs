using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Workflows.Elsa.Activities;
using Elsa.Workflows.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Workflows.Elsa;

/// <summary>What a trigger or bookmark waits for; Elsa matches stimuli by the hash of these values.</summary>
public sealed record StateStimulus(string EntityType, string State);

public sealed record ApprovalStimulus(string EntityType, Guid EntityId);

public sealed record EntityChangeStimulus(string EntityType, string Action);

/// <summary>A watched entity was created, updated or deleted; written to the outbox in the same transaction.</summary>
public sealed record EntityChanged(string EntityType, Guid EntityId, string Action) : IDomainEvent;

/// <summary>Hands workflow transitions, decisions and entity changes to Elsa after the commit (outbox), with their data as workflow input.</summary>
internal sealed class ElsaStimuli(IStimulusSender sender, ICurrentUser currentUser)
    : IDomainEventHandler<WorkflowTransitioned>, IDomainEventHandler<ApprovalDecided>, IDomainEventHandler<EntityChanged>
{
    public Task HandleAsync(WorkflowTransitioned e, CancellationToken cancellationToken) =>
        SendAsync<WorkflowStateReached>(new StateStimulus(e.EntityType, e.To), new()
        {
            [ElsaInputs.EntityType] = e.EntityType, [ElsaInputs.EntityId] = e.EntityId, [ElsaInputs.From] = e.From, [ElsaInputs.To] = e.To,
            [ElsaInputs.Transition] = e.Transition, [ElsaInputs.Comment] = e.Comment,
        }, cancellationToken);

    public Task HandleAsync(ApprovalDecided e, CancellationToken cancellationToken) =>
        SendAsync<WaitForApproval>(new ApprovalStimulus(e.EntityType, e.EntityId), new()
        {
            [ElsaInputs.EntityType] = e.EntityType, [ElsaInputs.EntityId] = e.EntityId, [ElsaInputs.Status] = e.Status.ToString(), [ElsaInputs.Comment] = e.Comment,
        }, cancellationToken);

    public Task HandleAsync(EntityChanged e, CancellationToken cancellationToken) =>
        SendAsync<EntityChangedTrigger>(new EntityChangeStimulus(e.EntityType, e.Action), new()
        {
            [ElsaInputs.EntityType] = e.EntityType, [ElsaInputs.EntityId] = e.EntityId, [ElsaInputs.Action] = e.Action,
        }, cancellationToken);

    private async Task SendAsync<TActivity>(object stimulus, Dictionary<string, object?> input, CancellationToken cancellationToken)
        where TActivity : global::Elsa.Workflows.IActivity
    {
        input[ElsaInputs.UserId] = currentUser.UserId;
        input[ElsaInputs.TenantId] = currentUser.TenantId;
        input[ElsaInputs.ImpersonatorId] = currentUser.ImpersonatorId;
        await sender.SendAsync(global::Elsa.Workflows.Helpers.ActivityTypeNameHelper.GenerateTypeName<TActivity>(), stimulus, new StimulusMetadata { Input = input! }, cancellationToken);
    }
}

/// <summary>Writes an <see cref="EntityChanged"/> to the outbox for every change of a watched entity type, in the same save.</summary>
internal sealed class EntityChangeInterceptor(ElsaWorkflowOptions options, ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null || options.Watched.Count == 0)
        {
            return;
        }

        var changes = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && options.Watched.ContainsKey(e.Metadata.ClrType))
            .Select(e => (Anonymous: e.Entity is IAnonymous, Change: new EntityChanged(options.Watched[e.Metadata.ClrType], (Guid)e.Property("Id").CurrentValue!, e.State == EntityState.Added ? "Created" : e.State == EntityState.Deleted ? "Deleted" : "Updated")))
            .ToList();
        context.Set<OutboxMessage>().AddRange(changes.Select(c => new OutboxMessage
        {
            Type = typeof(EntityChanged).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(c.Change),
            OccurredAt = clock.GetUtcNow(),
            TenantId = currentUser.TenantId,
            ActorId = c.Anonymous ? null : currentUser.UserId,
            ImpersonatorId = c.Anonymous ? null : currentUser.ImpersonatorId,
        }));
    }
}

/// <summary>The workflow input names the Coworkee triggers fill.</summary>
public static class ElsaInputs
{
    public const string EntityType = "EntityType";
    public const string EntityId = "EntityId";
    public const string From = "From";
    public const string To = "To";
    public const string Transition = "Transition";
    public const string Comment = "Comment";
    public const string Status = "Status";
    public const string Action = "Action";
    public const string UserId = "UserId";
    public const string TenantId = "TenantId";
    public const string ImpersonatorId = "ImpersonatorId";
}
