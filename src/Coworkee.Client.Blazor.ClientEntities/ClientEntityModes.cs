using System.Text.Json;
using System.Text.Json.Serialization;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>The user's choice of client or server per mirrored set, kept in the user setting <see cref="TableSettings.ClientEntityModes"/>.</summary>
internal sealed class ClientEntityModes(ICoworkeeApi api, ILogger<ClientEntityModes> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private Task<Dictionary<string, ClientEntityMode>>? _loading;

    public async Task<ClientEntityMode> GetAsync(string entitySet) => (await AllAsync()).GetValueOrDefault(entitySet);

    public async Task SetAsync(string entitySet, ClientEntityMode mode)
    {
        var all = await AllAsync();
        all[entitySet] = mode;
        try
        {
            await api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?> { [TableSettings.ClientEntityModes] = JsonSerializer.Serialize(all, Json) });
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            // signed out or without the settings module: the choice lasts for this visit
            logger.LogDebug(exception, "Keeping the client entity mode of {EntitySet} failed", entitySet);
        }
    }

    private Task<Dictionary<string, ClientEntityMode>> AllAsync() => _loading ??= LoadAsync();

    private async Task<Dictionary<string, ClientEntityMode>> LoadAsync()
    {
        try
        {
            var value = ((await api.GetSettingsAsync(SettingScope.User)) ?? []).FirstOrDefault(s => s.Name == TableSettings.ClientEntityModes)?.Value;
            return value is { Length: > 0 }
                ? new(JsonSerializer.Deserialize<Dictionary<string, ClientEntityMode>>(value, Json) ?? [], StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or JsonException)
        {
            logger.LogDebug(exception, "No stored client entity modes");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
