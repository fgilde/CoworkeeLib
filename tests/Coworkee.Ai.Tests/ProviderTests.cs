using System.Net;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Ai.Tests;

public sealed class ProviderTests(AiApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Function_calls_of_any_chat_client_run_as_tools_and_return_their_results()
    {
        var fake = new FakeChatClient();
        // OpenAI compatible servers may end a tool turn with "stop"
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("c1", "add_note", new Dictionary<string, object?> { ["title"] = "Milk" })])) { FinishReason = ChatFinishReason.Stop });
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Added Milk.")) { FinishReason = ChatFinishReason.Stop });

        var answer = await ChatAsync(fake, "Note milk");

        answer.Value!.Text.ShouldBe("Added Milk.");
        answer.Value.StopReason.ShouldBe("end_turn");
        answer.Value.Calls.Single().Tool.ShouldBe("add_note");
        (await app.InDbAsync(db => db.Set<Note>().Select(n => n.Title).ToListAsync(Ct))).ShouldBe(["Milk"]);
        fake.Connection.ShouldBe(new AiConnection(AiProviders.Anthropic, null, null, "test-key"));
        var tool = fake.Options[0].Tools!.OfType<AIFunctionDeclaration>().Single(t => t.Name == "add_note");
        tool.JsonSchema.GetProperty("properties").TryGetProperty("title", out _).ShouldBeTrue();
        var result = fake.Requests[1][^1].Contents.OfType<FunctionResultContent>().Single();
        (result.CallId, result.Exception).ShouldBe(("c1", null));
        Guid.TryParse(JsonSerializer.Deserialize<string>((string)result.Result!), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_tools_reach_the_model_as_errors()
    {
        var fake = new FakeChatClient();
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("c1", "fail_note", new Dictionary<string, object?> { ["title"] = "x" })])) { FinishReason = ChatFinishReason.ToolCalls });
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Not allowed.")));

        var answer = await ChatAsync(fake, "Note x");

        answer.Value!.Calls.Single().Succeeded.ShouldBeFalse();
        fake.Requests[1][^1].Contents.OfType<FunctionResultContent>().Single().Exception.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(AiProviders.Anthropic, null, "https://api.anthropic.com/v1/messages", "x-api-key")]
    [InlineData(AiProviders.OpenAI, null, "https://api.openai.com/v1/chat/completions", "Authorization")]
    [InlineData(AiProviders.AzureOpenAI, "https://acme.openai.azure.com", "https://acme.openai.azure.com/openai/v1/chat/completions", "Authorization")]
    [InlineData(AiProviders.Ollama, null, "http://localhost:11434/v1/chat/completions", null)]
    [InlineData(AiProviders.Ollama, "http://ollama:11434/", "http://ollama:11434/v1/chat/completions", null)]
    [InlineData(AiProviders.OpenAICompatible, "https://llm.example/v1", "https://llm.example/v1/chat/completions", null)]
    public async Task Each_provider_talks_to_its_endpoint(string provider, string? endpoint, string expected, string? keyHeader)
    {
        var handler = new RecordingHandler();
        var factory = new AiChatClientFactory(new SingleHttpClientFactory(handler));
        var connection = new AiConnection(provider, "model-x", endpoint, keyHeader is null ? null : "secret");
        connection.IsComplete.ShouldBeTrue();

        using var client = factory.Create(connection);
        await Should.ThrowAsync<Exception>(() => client.GetResponseAsync("hi", cancellationToken: Ct));

        handler.Uri!.ToString().ShouldBe(expected);
        handler.Body!.ShouldContain("model-x");
        if (keyHeader is not null)
        {
            handler.Headers.ShouldContain(h => string.Equals(h.Key, keyHeader, StringComparison.OrdinalIgnoreCase) && h.Value.Any(v => v.Contains("secret")));
        }
    }

    [Theory]
    [InlineData(AiProviders.Anthropic, null, null, null, false)]
    [InlineData(AiProviders.Anthropic, null, null, "key", true)]
    [InlineData(AiProviders.OpenAI, "", null, "key", true)]
    [InlineData(AiProviders.AzureOpenAI, "deployment", null, "key", false)]
    [InlineData(AiProviders.AzureOpenAI, null, "https://acme.openai.azure.com", "key", false)]
    [InlineData(AiProviders.AzureOpenAI, "deployment", "https://acme.openai.azure.com", "key", true)]
    [InlineData(AiProviders.Ollama, null, null, null, true)]
    [InlineData(AiProviders.OpenAICompatible, "model", null, null, false)]
    [InlineData(AiProviders.OpenAICompatible, "model", "http://localhost:1234/v1", null, true)]
    [InlineData("gemini", "model", "http://localhost", "key", false)]
    public void A_connection_is_complete_with_what_its_provider_needs(string provider, string? model, string? endpoint, string? key, bool complete) =>
        new AiConnection(provider, model, endpoint, key).IsComplete.ShouldBe(complete);

    private async Task<Coworkee.Core.Results.Result<AiTurn>> ChatAsync(FakeChatClient fake, string text)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var chat = ActivatorUtilities.CreateInstance<AiChat>(scope.ServiceProvider, (IAiChatClientFactory)fake);
        var connection = await chat.ConnectAsync(Ct);
        return await chat.RunAsync(connection.Value, [new ChatMessage(ChatRole.User, text)], null, _ => Task.CompletedTask, Ct);
    }
}

public sealed class FakeChatClient : IChatClient, IAiChatClientFactory
{
    public Queue<ChatResponse> Responses { get; } = new();

    public List<List<ChatMessage>> Requests { get; } = [];

    public List<ChatOptions> Options { get; } = [];

    public AiConnection? Connection { get; private set; }

    public IChatClient Create(AiConnection connection)
    {
        Connection = connection;
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add([.. messages]);
        Options.Add(options!);
        return Task.FromResult(Responses.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    public Uri? Uri { get; private set; }

    public string? Body { get; private set; }

    public List<KeyValuePair<string, IEnumerable<string>>> Headers { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri = request.RequestUri;
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Headers.AddRange(request.Headers);
        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}

internal sealed class SingleHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
