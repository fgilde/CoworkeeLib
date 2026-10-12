using System.Globalization;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>
/// The AI settings as the AI page edits them. The stored key is never sent to the browser: it stays while provider and endpoint stay,
/// a typed key replaces it, and switching the provider or endpoint drops it so it never reaches another server.
/// </summary>
public sealed class AiConnectionForm
{
    private string _savedProvider = AiProviderCatalog.Anthropic;
    private string? _savedEndpoint;
    private bool _hasStoredKey;

    public bool Enabled { get; set; }

    public string Provider { get; set; } = AiProviderCatalog.Anthropic;

    public string? Model { get; set; }

    public string? Endpoint { get; set; }

    /// <summary>A key typed to replace the stored one.</summary>
    public string? NewKey { get; set; }

    public bool RemoveKey { get; set; }

    public string? Headers { get; set; }

    public string? AuthHeader { get; set; }

    public double? Temperature { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? MaxToolRounds { get; set; }

    public AiProviderInfo Info => AiProviderCatalog.Find(Provider) ?? AiProviderCatalog.All[0];

    public bool HasStoredKey => _hasStoredKey;

    /// <summary>The stored key applies: nothing typed, not removed, same provider and endpoint.</summary>
    public bool KeepsStoredKey => _hasStoredKey && !RemoveKey && string.IsNullOrWhiteSpace(NewKey) && Provider == _savedProvider && Trim(Endpoint) == _savedEndpoint;

    /// <summary>A stored key that will be dropped on save because the provider or endpoint changed.</summary>
    public bool DropsStoredKey => _hasStoredKey && !RemoveKey && string.IsNullOrWhiteSpace(NewKey) && !KeepsStoredKey;

    public bool HasKey => !string.IsNullOrWhiteSpace(NewKey) || KeepsStoredKey;

    public bool IsComplete => Info.IsComplete(Model, Endpoint, HasKey);

    /// <summary>
    /// The stored values over the <paramref name="defaults"/> of the configuration; <paramref name="hasKey"/> says whether the server
    /// has a key, stored or configured (user secrets), which never reaches the browser.
    /// </summary>
    public static AiConnectionForm From(IReadOnlyList<SettingValueDto> values, IReadOnlyDictionary<string, string?>? defaults = null, bool? hasKey = null)
    {
        string? Get(string name) => values.FirstOrDefault(v => v.Name == name) is { HasValue: true } stored
            ? stored.Value is { Length: > 0 } value ? value : null
            : defaults?.GetValueOrDefault(name) is { Length: > 0 } configured ? configured : null;
        var form = new AiConnectionForm
        {
            Enabled = bool.TryParse(Get(AiSettingNames.Enabled), out var enabled) && enabled,
            Provider = AiProviderCatalog.Find(Get(AiSettingNames.Provider))?.Id ?? AiProviderCatalog.Anthropic,
            Model = Get(AiSettingNames.Model),
            Endpoint = Get(AiSettingNames.Endpoint),
            Headers = Get(AiSettingNames.Headers),
            AuthHeader = Get(AiSettingNames.AuthHeader),
            Temperature = double.TryParse(Get(AiSettingNames.Temperature), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null,
            MaxOutputTokens = int.TryParse(Get(AiSettingNames.MaxOutputTokens), CultureInfo.InvariantCulture, out var max) ? max : null,
            MaxToolRounds = int.TryParse(Get(AiSettingNames.MaxToolRounds), CultureInfo.InvariantCulture, out var rounds) ? rounds : null,
        };
        form._savedProvider = form.Provider;
        form._savedEndpoint = Trim(form.Endpoint);
        form._hasStoredKey = hasKey ?? values.Any(v => v.Name == AiSettingNames.ApiKey && v.HasValue);
        return form;
    }

    /// <summary>The settings to save; null values fall back to the defaults.</summary>
    public IReadOnlyDictionary<string, string?> ToSettings()
    {
        var values = new Dictionary<string, string?>
        {
            [AiSettingNames.Enabled] = Enabled ? "true" : "false",
            [AiSettingNames.Provider] = Provider,
            [AiSettingNames.Model] = Trim(Model),
            [AiSettingNames.Endpoint] = Trim(Endpoint),
            [AiSettingNames.Headers] = Trim(Headers),
            [AiSettingNames.AuthHeader] = Trim(AuthHeader),
            [AiSettingNames.Temperature] = Temperature?.ToString(CultureInfo.InvariantCulture),
            [AiSettingNames.MaxOutputTokens] = MaxOutputTokens?.ToString(CultureInfo.InvariantCulture),
            [AiSettingNames.MaxToolRounds] = MaxToolRounds?.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(NewKey))
        {
            values[AiSettingNames.ApiKey] = NewKey.Trim();
        }
        else if (_hasStoredKey && !KeepsStoredKey)
        {
            values[AiSettingNames.ApiKey] = null;
        }

        return values;
    }

    /// <summary>The draft for model lists and tests: a null key lets the server take the stored one.</summary>
    public AiConnectionDraft ToDraft() =>
        new(Provider, Trim(Model), Trim(Endpoint), !string.IsNullOrWhiteSpace(NewKey) ? NewKey.Trim() : KeepsStoredKey ? null : string.Empty, Trim(Headers), Trim(AuthHeader));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
