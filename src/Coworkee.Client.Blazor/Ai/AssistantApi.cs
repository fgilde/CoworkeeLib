using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantApi(HttpClient http) : ApiClientBase(http), IAssistantApi
{
    private const string Sessions = "api/v1/ai/sessions";

    public Task<AiStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) => GetAsync<AiStatusDto>("api/v1/ai/status", cancellationToken);

    public Task<IReadOnlyList<AiSessionDto>> GetSessionsAsync(string? search, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<AiSessionDto>>(string.IsNullOrWhiteSpace(search) ? Sessions : $"{Sessions}?search={Uri.EscapeDataString(search.Trim())}", cancellationToken);

    public Task<AiSessionDetailDto> GetSessionAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<AiSessionDetailDto>($"{Sessions}/{id}", cancellationToken);

    public Task<AiSessionDto> CreateSessionAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AiSessionDto>(HttpMethod.Post, Sessions, new AiSessionRequest(null), cancellationToken);

    public Task RenameSessionAsync(Guid id, string title, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Sessions}/{id}", new AiSessionRequest(title), cancellationToken);

    public Task DeleteSessionAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Sessions}/{id}", null, cancellationToken);

    public IAsyncEnumerable<AiStreamEvent> SendAsync(Guid id, string text, CancellationToken cancellationToken = default) =>
        StreamAsync<AiStreamEvent>($"{Sessions}/{id}/messages", new AiSendRequest(text), cancellationToken);

    public IAsyncEnumerable<AiStreamEvent> ConfirmAsync(Guid id, bool approve, CancellationToken cancellationToken = default) =>
        StreamAsync<AiStreamEvent>($"{Sessions}/{id}/confirm", new AiConfirmRequest(approve), cancellationToken);
}
