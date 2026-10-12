using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>Model lists and connection tests for the AI page; the connection itself is saved as AI settings.</summary>
public interface IAiConnectionApi
{
    Task<AiModelListDto> ListModelsAsync(AiConnectionDraft draft, CancellationToken cancellationToken = default);

    Task<AiConnectionTestDto> TestAsync(AiConnectionDraft draft, CancellationToken cancellationToken = default);
}
