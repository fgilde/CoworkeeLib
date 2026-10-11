using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Coworkee.Ai;

public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string OpenAI = "openAI";
    public const string AzureOpenAI = "azureOpenAI";
    public const string Ollama = "ollama";
    public const string OpenAICompatible = "openAICompatible";

    public const string OllamaEndpoint = "http://localhost:11434";

    public static readonly string[] All = [Anthropic, OpenAI, AzureOpenAI, Ollama, OpenAICompatible];

    /// <summary>The model when the setting is empty; Azure deployments and other endpoints have no default.</summary>
    public static string? DefaultModel(string provider) => provider switch
    {
        Anthropic => AiSettings.DefaultModel,
        OpenAI => "gpt-5",
        Ollama => "llama3.2",
        _ => null,
    };
}

/// <summary>Provider, model, endpoint and key of the assistant, read from the AI settings.</summary>
public sealed record AiConnection(string Provider, string? Model, string? Endpoint, string? ApiKey)
{
    public string? EffectiveModel => string.IsNullOrWhiteSpace(Model) ? AiProviders.DefaultModel(Provider) : Model;

    public bool IsComplete =>
        AiProviders.All.Contains(Provider)
        && EffectiveModel is not null
        && (Provider is AiProviders.Ollama or AiProviders.OpenAICompatible || !string.IsNullOrWhiteSpace(ApiKey))
        && (Provider is not (AiProviders.AzureOpenAI or AiProviders.OpenAICompatible) || Uri.TryCreate(Endpoint, UriKind.Absolute, out _));

    // the key never reaches a log
    public override string ToString() => $"{Provider} {EffectiveModel} {Endpoint}";
}

/// <summary>Creates the chat client of a connection. Replace it to bring another <see cref="IChatClient"/> (Gemini, Bedrock, a gateway).</summary>
public interface IAiChatClientFactory
{
    IChatClient Create(AiConnection connection);
}

internal sealed class AiChatClientFactory(IHttpClientFactory httpClients) : IAiChatClientFactory
{
    public const int MaxOutputTokens = 16_000;

    public IChatClient Create(AiConnection connection)
    {
        var http = httpClients.CreateClient(AiChat.HttpClientName);
        var model = connection.EffectiveModel!;
        var endpoint = connection.Endpoint is { Length: > 0 } value ? value.TrimEnd('/') : null;
        return connection.Provider switch
        {
            AiProviders.Anthropic => Anthropic(connection.ApiKey!, endpoint, http).AsIChatClient(model, MaxOutputTokens),
            AiProviders.OpenAI => OpenAI(connection.ApiKey!, endpoint, model, http),
            // the v1 API of Azure OpenAI speaks the OpenAI protocol; the model is the deployment name
            AiProviders.AzureOpenAI => OpenAI(connection.ApiKey!, endpoint!.Contains("/openai/", StringComparison.Ordinal) ? endpoint : endpoint + "/openai/v1", model, http),
            AiProviders.Ollama => OpenAI("ollama", (endpoint ?? AiProviders.OllamaEndpoint) is var ollama && ollama.EndsWith("/v1", StringComparison.Ordinal) ? ollama : ollama + "/v1", model, http),
            AiProviders.OpenAICompatible => OpenAI(connection.ApiKey is { Length: > 0 } key ? key : "none", endpoint, model, http),
            _ => throw new InvalidOperationException($"Unknown AI provider '{connection.Provider}'."),
        };
    }

    private static AnthropicClient Anthropic(string apiKey, string? endpoint, HttpClient http) =>
        new() { ApiKey = apiKey, HttpClient = http, BaseUrl = endpoint ?? "https://api.anthropic.com" };

    private static IChatClient OpenAI(string apiKey, string? endpoint, string model, HttpClient http)
    {
        var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) };
        if (endpoint is not null)
        {
            options.Endpoint = new Uri(endpoint + "/");
        }

        return new ChatClient(model, new ApiKeyCredential(apiKey), options).AsIChatClient();
    }
}
