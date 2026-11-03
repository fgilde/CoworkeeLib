namespace Coworkee.Contracts.Ai;

/// <summary>
/// One provider the assistant can use: where it answers (<see cref="BaseUrl"/>), what it needs, models to start with
/// (<see cref="Models"/>, the first is the default) and where to get a key.
/// </summary>
public sealed record AiProviderInfo(
    string Id,
    string Name,
    AiProtocol Protocol,
    string? BaseUrl,
    AiField Key,
    AiField Endpoint,
    IReadOnlyList<string> Models,
    string? KeyUrl,
    string Notes,
    string Color,
    string? Logo = null,
    bool Local = false,
    bool FreeTier = false)
{
    public string? DefaultModel => Models.Count > 0 ? Models[0] : null;

    /// <summary>Stands in for a logo.</summary>
    public string Monogram => Name[..1];

    public bool IsComplete(string? model, string? endpoint, bool hasKey) =>
        (DefaultModel is not null || !string.IsNullOrWhiteSpace(model))
        && (Key != AiField.Required || hasKey)
        && (Endpoint != AiField.Required || WebAddress.IsWeb(endpoint));
}
