using Coworkee.Contracts.Services;

namespace Coworkee.SourceGenerators.Tests.Sample;

public interface ITodoAppService : IApplicationService
{
    Task<TodoDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoDto>> GetListAsync(TodoFilter filter, CancellationToken cancellationToken = default);

    Task<int> CountAsync(string? text, IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    Task<TodoDto> CreateAsync(SaveTodo input, CancellationToken cancellationToken = default);

    Task<TodoDto> UpdateAsync(Guid id, SaveTodo input, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    ValueTask<TodoDto> ArchiveAsync(Guid id);

    [ServiceOperation(Method = "PUT", Route = "by-title/{title}")]
    Task<TodoDto> RenameAsync(string title, [ServiceParameter(ParameterSource.Query, Name = "to")] string newTitle, CancellationToken cancellationToken = default);

    [ServiceOperation(Ignore = true)]
    Task<string> LocalOnlyAsync();
}

public sealed record TodoDto(Guid Id, string Title, bool Done, bool Archived);

public sealed record SaveTodo(string Title, bool Done);

public sealed record TodoFilter(string? Text, bool? Done, DateTimeOffset? Since);
