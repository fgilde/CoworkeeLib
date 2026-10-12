using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AiConnectionApi(HttpClient http) : ApiClientBase(http), IAiConnectionApi
{
    public Task<AiModelListDto> ListModelsAsync(AiConnectionDraft draft, CancellationToken cancellationToken = default) =>
        SendAsync<AiModelListDto>(HttpMethod.Post, "api/v1/ai/connection/models", draft, cancellationToken);

    public Task<AiConnectionTestDto> TestAsync(AiConnectionDraft draft, CancellationToken cancellationToken = default) =>
        SendAsync<AiConnectionTestDto>(HttpMethod.Post, "api/v1/ai/connection/test", draft, cancellationToken);
}
