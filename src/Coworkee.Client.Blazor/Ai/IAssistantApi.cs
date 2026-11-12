using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

public interface IAssistantApi
{
    Task<AiStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiSessionDto>> GetSessionsAsync(string? search, CancellationToken cancellationToken = default);

    Task<AiSessionDetailDto> GetSessionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AiSessionDto> CreateSessionAsync(CancellationToken cancellationToken = default);

    Task RenameSessionAsync(Guid id, string title, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The answer as it arrives; the last event carries the saved message or the error. <paramref name="ui"/> is what the user sees.</summary>
    IAsyncEnumerable<AiStreamEvent> SendAsync(Guid id, string text, AiUiContext? ui, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AiStreamEvent> ConfirmAsync(Guid id, bool approve, AiUiContext? ui, CancellationToken cancellationToken = default);

    /// <summary>Answers a <see cref="AiStreamEvent.ClientQuery"/> of the running turn of conversation <paramref name="id"/>.</summary>
    Task SendClientResultAsync(Guid id, AiClientResult result, CancellationToken cancellationToken = default);
}
