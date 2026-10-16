using Coworkee.Contracts.Workflows;

namespace Coworkee.Client.Blazor.Workflows;

/// <summary>Workflow states of table rows: the rows of one render pass ask together, one request per entity type and up to 200 rows.</summary>
public sealed class WorkflowStates(IWorkflowApi api)
{
    private const int MaxBatch = 200;
    private readonly Dictionary<string, Batch> _batches = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public async Task<WorkflowStatusDto?> GetAsync(string entityType, Guid entityId)
    {
        Task<IReadOnlyDictionary<Guid, WorkflowStatusDto>> states;
        lock (_gate)
        {
            if (!_batches.TryGetValue(entityType, out var batch) || batch.Ids.Count >= MaxBatch)
            {
                batch = new Batch();
                _batches[entityType] = batch;
                _ = SendAsync(entityType, batch);
            }

            batch.Ids.Add(entityId);
            states = batch.Done.Task;
        }

        return (await states).GetValueOrDefault(entityId);
    }

    // collects the rows of one render pass for 30 ms, like the user cards
    private async Task SendAsync(string entityType, Batch batch)
    {
        await Task.Delay(30);
        List<Guid> ids;
        lock (_gate)
        {
            if (_batches.GetValueOrDefault(entityType) == batch)
            {
                _batches.Remove(entityType);
            }

            ids = [.. batch.Ids];
        }

        try
        {
            batch.Done.SetResult((await api.GetStatesAsync(entityType, ids)).ToDictionary(s => s.EntityId));
        }
        catch (Exception exception)
        {
            batch.Done.SetException(exception);
        }
    }

    private sealed class Batch
    {
        public HashSet<Guid> Ids { get; } = [];

        public TaskCompletionSource<IReadOnlyDictionary<Guid, WorkflowStatusDto>> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
