using System.Text.Json;
using Coworkee.Contracts.Ai;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai.Tests;

public sealed class CatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Providers => [.. AiProviderCatalog.Ids];

    [Fact]
    public void The_catalog_offers_the_known_providers_with_what_the_settings_page_needs()
    {
        AiProviderCatalog.Ids.ShouldBe(
            ["anthropic", "openAI", "azureOpenAI", "gemini", "openRouter", "mistral", "groq", "deepSeek", "xai", "together", "fireworks", "ollama", "lmStudio", "openAICompatible"]);
        AiProviderCatalog.Ids.ShouldBeUnique();
        AiProviders.All.ShouldBe(AiProviderCatalog.Ids);
        foreach (var provider in AiProviderCatalog.All)
        {
            provider.Name.ShouldNotBeNullOrWhiteSpace();
            provider.Notes.ShouldNotBeNullOrWhiteSpace();
            provider.Color.ShouldMatch("^#[0-9A-F]{6}$");
            (provider.BaseUrl is not null || provider.Endpoint == AiField.Required).ShouldBeTrue(provider.Id);
            (provider.KeyUrl is not null || provider.Id == AiProviderCatalog.Custom).ShouldBeTrue(provider.Id);
            (provider.Key == AiField.None).ShouldBe(provider.Local, provider.Id);
            (provider.DefaultModel is not null).ShouldBe(provider.Id is not (AiProviderCatalog.AzureOpenAI or AiProviderCatalog.LmStudio or AiProviderCatalog.Custom), provider.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Each_provider_talks_to_its_address_with_its_key(string id)
    {
        var provider = AiProviderCatalog.Find(id)!;
        var endpoint = provider.BaseUrl ?? "https://llm.example/v1";
        var handler = new RecordingHandler();
        using var client = new AiChatClientFactory(new SingleHttpClientFactory(handler))
            .Create(new AiConnection(id, "model-x", provider.BaseUrl is null ? endpoint : null, "secret"));

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync("hi", cancellationToken: Ct));

        var path = provider.Protocol switch
        {
            AiProtocol.Anthropic => "/v1/messages",
            AiProtocol.AzureOpenAI => "/openai/v1/chat/completions",
            AiProtocol.Ollama => "/v1/chat/completions",
            _ => "/chat/completions",
        };
        handler.Uri!.ToString().ShouldBe(endpoint + path);
        handler.Body!.ShouldContain("model-x");
        var (header, value) = provider.Protocol == AiProtocol.Anthropic ? ("x-api-key", "secret") : ("Authorization", "Bearer secret");
        handler.Headers.ShouldContain(h => string.Equals(h.Key, header, StringComparison.OrdinalIgnoreCase) && h.Value.Contains(value));
    }

    [Fact]
    public async Task A_custom_server_gets_extra_headers_the_key_in_its_own_header_and_the_options()
    {
        var handler = new RecordingHandler();
        var connection = new AiConnection(AiProviderCatalog.Custom, "m", "https://gateway.acme.test/v1/", "secret")
        {
            Headers = "HTTP-Referer: https://acme.test\nX-Title: Acme app",
            AuthHeader = "api-key",
            Temperature = 0.5f,
            MaxOutputTokens = 500,
        };
        using var client = new AiChatClientFactory(new SingleHttpClientFactory(handler)).Create(connection);

        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync("hi", cancellationToken: Ct));

        handler.Uri!.ToString().ShouldBe("https://gateway.acme.test/v1/chat/completions");
        var headers = handler.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        headers["api-key"].ShouldBe("secret");
        headers["X-Title"].ShouldBe("Acme app");
        headers["HTTP-Referer"].ShouldBe("https://acme.test");
        headers.ShouldNotContainKey("Authorization");
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        body.GetProperty("temperature").GetSingle().ShouldBe(0.5f);
        body.EnumerateObject().ShouldContain(p => p.Name.StartsWith("max_") && p.Value.GetInt32() == 500);
    }

    [Theory]
    [InlineData("anthropic", null, "https://api.anthropic.com/v1/models?limit=1000", "x-api-key", "secret")]
    [InlineData("openAI", null, "https://api.openai.com/v1/models", "Authorization", "Bearer secret")]
    [InlineData("gemini", null, "https://generativelanguage.googleapis.com/v1beta/openai/models", "Authorization", "Bearer secret")]
    [InlineData("openRouter", null, "https://openrouter.ai/api/v1/models", "Authorization", "Bearer secret")]
    [InlineData("azureOpenAI", "https://acme.openai.azure.com/", "https://acme.openai.azure.com/openai/v1/models", "Authorization", "Bearer secret")]
    [InlineData("ollama", "http://ollama:11434/v1", "http://ollama:11434/api/tags", null, null)]
    [InlineData("lmStudio", null, "http://localhost:1234/v1/models", "Authorization", "Bearer secret")]
    public void Model_lists_come_from_the_list_endpoint_of_the_provider(string provider, string? endpoint, string expected, string? header, string? value)
    {
        using var request = AiEndpoints.ModelsRequest(new AiConnection(provider, null, endpoint, "secret"));

        request.RequestUri!.ToString().ShouldBe(expected);
        if (header is not null)
        {
            request.Headers.GetValues(header).ShouldBe([value!]);
        }
    }

    [Fact]
    public void A_key_header_replaces_the_bearer_token_in_the_model_list()
    {
        using var request = AiEndpoints.ModelsRequest(
            new AiConnection(AiProviderCatalog.Custom, null, "https://gw.test/v1", "secret") { AuthHeader = "x-key", Headers = "X-Title: Acme" });

        request.Headers.Authorization.ShouldBeNull();
        request.Headers.GetValues("x-key").ShouldBe(["secret"]);
        request.Headers.GetValues("X-Title").ShouldBe(["Acme"]);
    }

    [Theory]
    [InlineData("""{"data":[{"id":"gpt-5","object":"model"},{"id":"gpt-4.1"}]}""", "gpt-4.1,gpt-5")]
    [InlineData("""{"data":[{"id":"claude-opus-5-5","display_name":"Claude Opus 5.5"}],"has_more":false}""", "claude-opus-5-5")]
    [InlineData("""{"models":[{"name":"llama3.2:latest","model":"llama3.2:latest"}]}""", "llama3.2:latest")]
    [InlineData("""[{"id":"meta-llama/Llama-3.3-70B"},{"id":"Qwen/Qwen3"}]""", "meta-llama/Llama-3.3-70B,Qwen/Qwen3")]
    [InlineData("""{"object":"list","data":[{"id":"models/gemini-2.5-pro"}]}""", "gemini-2.5-pro")]
    [InlineData("""{"error":"nope"}""", "")]
    public void Model_lists_of_every_shape_are_read(string json, string expected) =>
        string.Join(",", AiEndpoints.ParseModels(JsonDocument.Parse(json).RootElement).Select(m => m.Id)).ShouldBe(expected);

    [Theory]
    [InlineData("X-Title: Acme\nHTTP-Referer: https://acme.test", null)]
    [InlineData("  \n", null)]
    [InlineData("no colon", "no colon")]
    [InlineData("Bad Name: x", "Bad Name: x")]
    public void Extra_headers_are_lines_of_name_and_value(string text, string? invalid) =>
        AiEndpoints.InvalidHeaderLine(text).ShouldBe(invalid);
}
