using Coworkee.Contracts.Ai;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai.Tests;

public sealed class ConnectionProbeTests
{
    private static readonly AiConnection Connection = new(AiProviderCatalog.OpenAI, null, null, "sk-secret-key");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_working_connection_reports_its_answer_latency_and_tool_calls()
    {
        var fake = new FakeChatClient();
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, " OK ")));
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("c1", "ping", new Dictionary<string, object?> { ["text"] = "pong" })])));

        var result = await Probe(fake).TestAsync(Connection, Ct);

        result.ShouldBe(new AiConnectionTestDto(true, result.LatencyMs, "OK", true, null));
        fake.Options[1].Tools!.Single().Name.ShouldBe("ping");
    }

    [Fact]
    public async Task A_model_that_does_not_call_tools_answers_with_a_warning()
    {
        var fake = new FakeChatClient();
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK")));
        fake.Responses.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong")));

        var result = await Probe(fake).TestAsync(Connection, Ct);

        (result.Succeeded, result.CallsTools).ShouldBe((true, false));
        result.Error!.ShouldContain("did not call");
    }

    [Fact]
    public async Task Failures_are_reported_without_the_key()
    {
        var fake = new FakeChatClient { Failure = new InvalidOperationException("401 Incorrect API key provided: sk-secret-key") };

        var result = await Probe(fake).TestAsync(Connection, Ct);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("401 Incorrect API key provided: ****");
    }

    [Fact]
    public async Task Model_lists_report_the_refusal_of_the_provider()
    {
        var handler = new RecordingHandler();

        var result = await new AiConnectionProbe(new SingleHttpClientFactory(handler), new FakeChatClient()).ListModelsAsync(Connection, Ct);

        result.Models.ShouldBeEmpty();
        result.Error!.ShouldStartWith("400 Bad Request");
        handler.Uri!.ToString().ShouldBe("https://api.openai.com/v1/models");
    }

    [Fact]
    public async Task An_unreachable_server_fails_at_once_without_asking_the_model()
    {
        var fake = new FakeChatClient();

        var result = await new AiConnectionProbe(new SingleHttpClientFactory(new UnreachableHandler()), fake).TestAsync(Connection, Ct);

        result.Succeeded.ShouldBeFalse();
        result.Error!.ShouldStartWith("Could not reach the server");
        fake.Requests.ShouldBeEmpty();
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (localhost:11434)");
    }

    private static AiConnectionProbe Probe(FakeChatClient fake) => new(new SingleHttpClientFactory(new RecordingHandler()), fake);
}
