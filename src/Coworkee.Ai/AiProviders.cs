using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Coworkee.Contracts.Ai;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Coworkee.Ai;

/// <summary>Ids of the common providers; <see cref="AiProviderCatalog"/> has all of them with their addresses and models.</summary>
public static class AiProviders
{
    public const string Anthropic = AiProviderCatalog.Anthropic;
    public const string OpenAI = AiProviderCatalog.OpenAI;
    public const string AzureOpenAI = AiProviderCatalog.AzureOpenAI;
    public const string Ollama = AiProviderCatalog.Ollama;
    public const string OpenAICompatible = AiProviderCatalog.Custom;

    public const string OllamaEndpoint = "http://localhost:11434";

    public static IReadOnlyList<string> All => AiProviderCatalog.Ids;

    /// <summary>The model when the setting is empty; Azure deployments and custom endpoints have no default.</summary>
    public static string? DefaultModel(string provider) => AiProviderCatalog.Find(provider)?.DefaultModel;
}

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

/// <summary>Creates the chat client of a connection. Replace it to bring another <see cref="IChatClient"/> (Bedrock, a gateway with its own SDK).</summary>
public interface IAiChatClientFactory
{
    IChatClient Create(AiConnection connection);
}

internal sealed class AiChatClientFactory(IHttpClientFactory httpClients) : IAiChatClientFactory
{
    public const int MaxOutputTokens = 16_000;

    public IChatClient Create(AiConnection connection)
    {
        var info = connection.Info ?? throw new InvalidOperationException($"Unknown AI provider '{connection.Provider}'.");
        var http = httpClients.CreateClient(AiChat.HttpClientName);
        var model = connection.EffectiveModel!;
        var client = info.Protocol == AiProtocol.Anthropic
            ? new AnthropicClient { ApiKey = connection.ApiKey!, HttpClient = http, BaseUrl = connection.EffectiveEndpoint! }
                .AsIChatClient(model, connection.MaxOutputTokens ?? MaxOutputTokens)
            : OpenAI(connection, model, http);
        return connection.Temperature is null && connection.MaxOutputTokens is null ? client : new AiDefaultOptionsClient(client, connection);
    }

    private static IChatClient OpenAI(AiConnection connection, string model, HttpClient http)
    {
        var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http), Endpoint = new Uri(AiEndpoints.OpenAIBase(connection) + "/") };
        options.AddPolicy(new AiHeadersPolicy(connection), PipelinePosition.BeforeTransport);

        // keyless local servers still get a token: the SDK requires one
        var key = connection.ApiKey is { Length: > 0 } value ? value : "none";
        return new ChatClient(model, new ApiKeyCredential(key), options).AsIChatClient();
    }
}

/// <summary>Adds the extra headers of a connection and, with an auth header name, sends the key there instead of as bearer token.</summary>
internal sealed class AiHeadersPolicy(AiConnection connection) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message.Request);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message.Request);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Apply(PipelineRequest request)
    {
        if (connection.AuthHeader is { Length: > 0 })
        {
            request.Headers.Remove("Authorization");
        }

        foreach (var (name, value) in AiEndpoints.Headers(connection))
        {
            request.Headers.Set(name, value);
        }
    }
}
