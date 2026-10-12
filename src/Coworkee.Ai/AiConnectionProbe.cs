using System.Diagnostics;
using System.Text.Json;
using Coworkee.Contracts.Ai;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>Lists the models of a connection and tries it with a short prompt and a tool call, for the AI page.</summary>
internal sealed class AiConnectionProbe(IHttpClientFactory httpClients, IAiChatClientFactory clients)
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(90);

    private static readonly AIFunctionDeclaration Ping = AIFunctionFactory.CreateDeclaration("ping", "Checks that tools work; returns pong.",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { text = new { type = "string" } }, required = new[] { "text" } }));

    public async Task<AiModelListDto> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListTimeout);
        try
        {
            using var request = AiEndpoints.ModelsRequest(connection);
            using var response = await httpClients.CreateClient(AiChat.HttpClientName).SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                return new AiModelListDto([], Describe(connection, $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}"));
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var models = AiEndpoints.ParseModels(json.RootElement);
            return new AiModelListDto(models, models.Count == 0 ? "The provider listed no models." : null);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new AiModelListDto([], Describe(connection, exception));
        }
    }

    public async Task<AiConnectionTestDto> TestAsync(AiConnection connection, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TestTimeout);
        using var client = clients.Create(connection);
        var watch = Stopwatch.StartNew();
        string reply;
        try
        {
            reply = (await client.GetResponseAsync("Reply with the single word OK.", cancellationToken: timeout.Token)).Text.Trim();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new AiConnectionTestDto(false, (int)watch.ElapsedMilliseconds, null, false, Describe(connection, exception));
        }

        var latency = (int)watch.ElapsedMilliseconds;
        try
        {
            var probe = await client.GetResponseAsync("Call the tool ping with the text pong. Do not answer in text.", new ChatOptions { Tools = [Ping] }, timeout.Token);
            var calls = probe.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any(c => c.Name == Ping.Name);
            return new AiConnectionTestDto(true, latency, Cut(reply), calls, calls ? null : "The model answered but did not call the test tool; the assistant needs tool calls.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new AiConnectionTestDto(true, latency, Cut(reply), false, "Tool calls failed: " + Describe(connection, exception));
        }
    }

    private static string Describe(AiConnection connection, Exception exception) => Describe(connection, exception switch
    {
        OperationCanceledException => "No answer in time.",
        HttpRequestException { StatusCode: null } => "Could not reach the server: " + exception.Message,
        _ => exception.Message,
    });

    // provider errors may echo the key
    private static string Describe(AiConnection connection, string message) =>
        Cut(connection.ApiKey is { Length: >= 4 } key ? message.Replace(key, "****", StringComparison.Ordinal) : message)!;

    private static string? Cut(string? text) => text is { Length: > 500 } ? text[..500] + "…" : text;
}
