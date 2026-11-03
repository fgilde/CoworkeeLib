using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

/// <summary>
/// Provider, model, endpoint and key of the assistant, read from the AI settings. An empty endpoint is the provider's base address;
/// <see cref="Headers"/> ("Name: value" lines) and <see cref="AuthHeader"/> apply to the OpenAI compatible providers.
/// </summary>
public sealed record AiConnection(string Provider, string? Model, string? Endpoint, string? ApiKey)
{
    public string? Headers { get; init; }

    public string? AuthHeader { get; init; }

    public float? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public AiProviderInfo? Info => AiProviderCatalog.Find(Provider);

    public string? EffectiveModel => string.IsNullOrWhiteSpace(Model) ? Info?.DefaultModel : Model.Trim();

    public string? EffectiveEndpoint => (string.IsNullOrWhiteSpace(Endpoint) ? Info?.BaseUrl : Endpoint.Trim())?.TrimEnd('/');

    public bool IsComplete => Info?.IsComplete(Model, Endpoint, !string.IsNullOrWhiteSpace(ApiKey)) == true;

    // the key never reaches a log
    public override string ToString() => $"{Provider} {EffectiveModel} {Endpoint}";
}
