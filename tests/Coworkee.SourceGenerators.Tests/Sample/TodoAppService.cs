using System.Collections.Concurrent;
using Coworkee.Application.Authorization;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.SourceGenerators.Tests.Sample;

internal sealed class TodoAppService(TodoStore store) : ITodoAppService
{
    public Task<TodoDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Todos.TryGetValue(id, out var todo) ? todo : throw new ErrorException(Error.NotFound("todo.not_found", "No such todo.")));

    public Task<IReadOnlyList<TodoDto>> GetListAsync(TodoFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TodoDto>>(store.Todos.Values
            .Where(t => (filter.Text is null || t.Title.Contains(filter.Text, StringComparison.Ordinal)) && (filter.Done is null || t.Done == filter.Done))
            .OrderBy(t => t.Title, StringComparer.Ordinal)
            .ToList());

    public Task<int> CountAsync(string? text, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Todos.Values.Count(t => (text is null || t.Title.Contains(text, StringComparison.Ordinal)) && (ids.Count == 0 || ids.Contains(t.Id))));

    public Task<TodoDto> CreateAsync(SaveTodo input, CancellationToken cancellationToken = default) => Task.FromResult(Save(Guid.NewGuid(), input));

    public Task<TodoDto> UpdateAsync(Guid id, SaveTodo input, CancellationToken cancellationToken = default) => Task.FromResult(Save(id, input));

    [RequiresPermission(TodoStore.DeletePermission)]
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Todos.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public async ValueTask<TodoDto> ArchiveAsync(Guid id) => store.Todos[id] = (await GetAsync(id)) with { Archived = true };

    public Task<TodoDto> RenameAsync(string title, string newTitle, CancellationToken cancellationToken = default)
    {
        var todo = store.Todos.Values.Single(t => t.Title == title) with { Title = newTitle };
        return Task.FromResult(store.Todos[todo.Id] = todo);
    }

    public Task<string> LocalOnlyAsync() => Task.FromResult("local");

    private TodoDto Save(Guid id, SaveTodo input) => store.Todos[id] = new TodoDto(id, input.Title, input.Done, false);
}

internal sealed class TodoStore
{
    public const string DeletePermission = "Todos.Delete";

    public ConcurrentDictionary<Guid, TodoDto> Todos { get; } = new();
}

internal sealed class SaveTodoValidator : AbstractValidator<SaveTodo>
{
    public SaveTodoValidator() => RuleFor(t => t.Title).NotEmpty();
}
