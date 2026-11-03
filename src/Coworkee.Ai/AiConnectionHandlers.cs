using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Ai;

internal sealed class AiConnectionHandlers(AiConnections connections, AiConnectionProbe probe, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<ListAiModels, Result<AiModelListDto>>, IHandler<TestAiConnection, Result<AiConnectionTestDto>>
{
    public async Task<Result<AiModelListDto>> HandleAsync(ListAiModels query, CancellationToken cancellationToken) =>
        await ResolveAsync(query.Draft, cancellationToken) is { } connection
            ? await probe.ListModelsAsync(connection, cancellationToken)
            : Forbidden;

    public async Task<Result<AiConnectionTestDto>> HandleAsync(TestAiConnection query, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(query.Draft, cancellationToken) is not { } connection)
        {
            return Forbidden;
        }

        return connection.IsComplete
            ? await probe.TestAsync(connection, cancellationToken)
            : new AiConnectionTestDto(false, 0, null, false, "The connection lacks a key, an endpoint or a model.");
    }

    private static readonly Error Forbidden = Error.Forbidden("ai.global_forbidden", "The AI connection can only be changed from the system organisation.");

    // the AI settings are system wide; the stored key only goes to the provider and endpoint it was saved for
    private async Task<AiConnection?> ResolveAsync(AiConnectionDraft draft, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId
            || services.GetService<ITenantDirectory>() is not { } tenants
            || !await tenants.IsSystemTenantAsync(tenantId, cancellationToken))
        {
            return null;
        }

        var saved = await connections.GetSavedAsync(cancellationToken);
        var draftConnection = new AiConnection(draft.Provider, draft.Model, draft.Endpoint, draft.ApiKey);
        var sameTarget = saved.Provider == draft.Provider && saved.EffectiveEndpoint == draftConnection.EffectiveEndpoint;
        return draftConnection with
        {
            ApiKey = draft.ApiKey ?? (sameTarget ? saved.ApiKey : null),
            Headers = draft.Headers,
            AuthHeader = draft.AuthHeader?.Trim(),
        };
    }
}
