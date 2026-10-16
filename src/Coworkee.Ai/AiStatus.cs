using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;
using Coworkee.Settings;

namespace Coworkee.Ai;

internal sealed class AiStatus(ISettingProvider settings) : ISystemStatusContributor
{
    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var enabled = await settings.GetAsync<bool>(AiSettings.Enabled, cancellationToken);
        var key = await settings.GetAsync(AiSettings.ApiKey, cancellationToken);
        var endpoint = await settings.GetAsync(AiSettings.Endpoint, cancellationToken);
        return new SystemStatusSection("ai", "AI assistant",
        [
            SystemStatusEntry.Text("Enabled", enabled ? "Yes" : "No", enabled ? SystemStatusLevel.Ok : SystemStatusLevel.Info),
            new("Provider", await settings.GetAsync(AiSettings.Provider, cancellationToken) ?? "-"),
            new("Model", await settings.GetAsync(AiSettings.Model, cancellationToken) is { Length: > 0 } model ? model : AiSettings.DefaultModel),
            SystemStatusEntry.Text("Connection", string.IsNullOrEmpty(key) && string.IsNullOrEmpty(endpoint) ? "No key or endpoint" : "Configured",
                enabled && string.IsNullOrEmpty(key) && string.IsNullOrEmpty(endpoint) ? SystemStatusLevel.Warning : SystemStatusLevel.Info),
        ], Link: "/admin/ai-settings", Order: 8);
    }
}
