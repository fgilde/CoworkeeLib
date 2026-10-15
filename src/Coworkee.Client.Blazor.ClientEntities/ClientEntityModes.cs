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

    private Task<Dictionary<string, ClientEntityMode>?>? _loading;

    // choices made while the stored ones could not be read: saving then would overwrite them
    private readonly Dictionary<string, ClientEntityMode> _unsaved = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ClientEntityMode> GetAsync(string entitySet)
    {
        var all = await AllAsync();
        return _unsaved.TryGetValue(entitySet, out var mode) ? mode : all?.GetValueOrDefault(entitySet) ?? default;
    }

    public async Task SetAsync(string entitySet, ClientEntityMode mode)
    {
        if (await AllAsync() is not { } all)
        {
            _unsaved[entitySet] = mode;
            return;
        }

        foreach (var (set, unsaved) in _unsaved)
        {
            all[set] = unsaved;
        }

        _unsaved.Clear();
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

    /// <summary>Forgets the choices, e.g. when another user signs in; the next call reads the stored ones again.</summary>
    public void Clear()
    {
        _loading = null;
        _unsaved.Clear();
    }

    // null when the stored choices could not be read; that is not kept, the next call tries again
    private async Task<Dictionary<string, ClientEntityMode>?> AllAsync()
    {
        var loading = _loading ??= LoadAsync();
        var all = await loading;
        if (all is null && _loading == loading)
        {
            _loading = null;
        }

        return all;
    }

    private async Task<Dictionary<string, ClientEntityMode>?> LoadAsync()
    {
        try
        {
            var value = ((await api.GetSettingsAsync(SettingScope.User)) ?? []).FirstOrDefault(s => s.Name == TableSettings.ClientEntityModes)?.Value;
            return value is { Length: > 0 }
                ? new(JsonSerializer.Deserialize<Dictionary<string, ClientEntityMode>>(value, Json) ?? [], StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            // an unreadable value holds no choice worth keeping
            logger.LogDebug(exception, "The stored client entity modes are unreadable");
            return new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            logger.LogDebug(exception, "No stored client entity modes");
            return null;
        }
    }
}
