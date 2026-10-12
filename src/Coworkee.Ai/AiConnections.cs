using System.Globalization;
using Coworkee.Contracts.Ai;
using Coworkee.Settings;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>
/// The connection of the AI settings. Read on every call (the settings are cached and dropped on change),
/// so a new provider, model or key applies to the next request without a restart.
/// </summary>
public sealed class AiConnections(ISettingProvider settings, IAiChatClientFactory clients)
{
    /// <summary>The saved connection, also while it is switched off or incomplete.</summary>
    public async Task<AiConnection> GetSavedAsync(CancellationToken cancellationToken = default) =>
        new(await settings.GetAsync(AiSettingNames.Provider, cancellationToken) is { Length: > 0 } provider ? provider : AiProviderCatalog.Anthropic,
            await settings.GetAsync(AiSettingNames.Model, cancellationToken),
            await settings.GetAsync(AiSettingNames.Endpoint, cancellationToken),
            await settings.GetAsync(AiSettingNames.ApiKey, cancellationToken))
        {
            Headers = await settings.GetAsync(AiSettingNames.Headers, cancellationToken),
            AuthHeader = await settings.GetAsync(AiSettingNames.AuthHeader, cancellationToken),
            Temperature = float.TryParse(await settings.GetAsync(AiSettingNames.Temperature, cancellationToken), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null,
            MaxOutputTokens = await settings.GetAsync<int?>(AiSettingNames.MaxOutputTokens, cancellationToken) is > 0 and var max ? max : null,
        };

    /// <summary>The connection the assistant uses now; null while the assistant is switched off or the connection lacks something.</summary>
    public async Task<AiConnection?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await settings.GetAsync<bool>(AiSettingNames.Enabled, cancellationToken) && await GetSavedAsync(cancellationToken) is { IsComplete: true } connection
            ? connection
            : null;

    /// <summary>A chat client on the active connection with its temperature and token limit; null while there is none. The caller disposes it.</summary>
    public async Task<IChatClient?> CreateClientAsync(CancellationToken cancellationToken = default) =>
        await GetActiveAsync(cancellationToken) is { } connection ? clients.Create(connection) : null;
}
