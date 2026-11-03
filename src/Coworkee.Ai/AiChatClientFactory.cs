using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Coworkee.Contracts.Ai;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Coworkee.Ai;

internal sealed class AiChatClientFactory(IHttpClientFactory httpClients) : IAiChatClientFactory
{
    public const int MaxOutputTokens = 16_000;

    public IChatClient Create(AiConnection connection)
    {
        var info = connection.Info ?? throw new InvalidOperationException($"Unknown AI provider '{connection.Provider}'.");
        var http = httpClients.CreateClient(AiChat.HttpClientName);
        var model = connection.EffectiveModel!;
        var client = info.Protocol == AiProtocol.Anthropic
            ? new AnthropicClient { ApiKey = connection.ApiKey!, HttpClient = http, BaseUrl = connection.EffectiveEndpoint!, Handlers = [new AnthropicProfileFilter()] }
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
