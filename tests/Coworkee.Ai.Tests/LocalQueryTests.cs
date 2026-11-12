using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai.Tests;

/// <summary>Reads of data sets the browser holds locally go to the browser first; the server runs them when it cannot or does not answer in time.</summary>
public sealed class LocalQueryTests(AiApp app) : IAsyncLifetime
{
    private const string Rows = """{"rows":[{"Tool":"from the browser"}],"total":1}""";

    private static readonly AiUiContext Local = new(LocalSets: ["AiToolCalls"]);

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static JsonObject QueryToolCalls => new() { ["entitySet"] = "aitoolcalls", ["select"] = "Tool", ["top"] = 5 };

    /// <summary>Sends a message and reads the stream line by line, as the browser does, calling <paramref name="onQuery"/> for every client query.</summary>
    private async Task<List<AiStreamEvent>> AskAsync(Guid session, AiUiContext? ui, Func<AiClientActionCall, Task>? onQuery = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/sessions/{session}/messages") { Content = JsonContent.Create(new AiSendRequest("How many?", ui)) };
        using var response = await Admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        var events = new List<AiStreamEvent>();
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<AiStreamEvent>(line, JsonSerializerOptions.Web)!;
            events.Add(item);
            if (item.Type == AiStreamEvent.ClientQuery && onQuery is not null)
            {
                await onQuery(item.Action!);
            }
        }

        return events;
    }

    private static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid session, string callId, string? output) =>
        client.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/client-results", new AiClientResult(callId, output), Ct);

    private async Task SetAsync(string name, string value) =>
        (await Admin.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?> { [name] = value }), Ct)).EnsureSuccessStatusCode();

    private Task<List<AiToolCall>> AuditAsync() => app.InDbAsync(db => db.Set<AiToolCall>().OrderBy(c => c.At).ToListAsync(Ct));

    private string ResultTheModelGot() => app.Claude.Requests.Last()["messages"]!.AsArray()[^1]!["content"]!.AsArray().Single()!["content"]!.ToJsonString();

    [Fact]
    public async Task A_read_of_a_local_set_is_answered_by_the_browser_and_audited_as_answered_there()
    {
        app.Claude.ToolUse(("query_data", QueryToolCalls));
        app.Claude.Text("One.");
        var session = await AiApp.NewSessionAsync(Admin);
        AiClientActionCall? asked = null;

        var events = await AskAsync(session, Local, async call =>
        {
            asked = call;
            (await AnswerAsync(Admin, session, call.CallId, Rows)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        });

        asked.ShouldNotBeNull().Name.ShouldBe("query_data");
        JsonNode.Parse(asked.Arguments)!["entitySet"]!.GetValue<string>().ShouldBe("aitoolcalls");
        var tool = events.Single(e => e.Type == AiStreamEvent.Tool).ToolCall!;
        (tool.Channel, tool.Output, tool.Succeeded).ShouldBe((AiChannels.ChatLocal, Rows, true));
        events[^1].Message!.Text.ShouldBe("One.");
        ResultTheModelGot().ShouldContain("from the browser");
        var audit = (await AuditAsync()).ShouldHaveSingleItem();
        (audit.Channel, audit.Tool, audit.UserId, audit.Succeeded).ShouldBe((AiChannels.ChatLocal, "query_data", (Guid?)_setup.AdminUserId, true));
        audit.Input.ShouldContain("aitoolcalls");
    }

    [Fact]
    public async Task Without_an_answer_in_time_the_server_runs_the_read()
    {
        await SetAsync(AiSettings.LocalQueryTimeout, "1");
        app.Claude.ToolUse(("query_data", QueryToolCalls));
        app.Claude.Text("None.");
        var timer = Stopwatch.StartNew();

        var events = await AskAsync(await AiApp.NewSessionAsync(Admin), Local);

        events.ShouldContain(e => e.Type == AiStreamEvent.ClientQuery);
        var tool = events.Single(e => e.Type == AiStreamEvent.Tool).ToolCall!;
        (tool.Channel, tool.Succeeded).ShouldBe((AiChannels.Chat, true));
        // the server tool's shape, the one the browser answers with
        tool.Output.ShouldBe("""{"rows":[],"total":0}""");
        timer.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(0.9));
        (await AuditAsync()).ShouldHaveSingleItem().Channel.ShouldBe(AiChannels.Chat);
    }

    [Fact]
    public async Task A_browser_that_cannot_answer_hands_the_read_back_at_once()
    {
        await SetAsync(AiSettings.LocalQueryTimeout, "30");
        app.Claude.ToolUse(("query_data", QueryToolCalls));
        app.Claude.Text("None.");
        var session = await AiApp.NewSessionAsync(Admin);
        var timer = Stopwatch.StartNew();

        var events = await AskAsync(session, Local, async call => (await AnswerAsync(Admin, session, call.CallId, null)).EnsureSuccessStatusCode());

        timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        events.Single(e => e.Type == AiStreamEvent.Tool).ToolCall!.Channel.ShouldBe(AiChannels.Chat);
        (await AuditAsync()).ShouldHaveSingleItem().Channel.ShouldBe(AiChannels.Chat);
    }

    [Fact]
    public async Task Sets_the_browser_does_not_hold_other_tools_and_the_switched_off_setting_stay_on_the_server()
    {
        var session = await AiApp.NewSessionAsync(Admin);
        app.Claude.ToolUse(("query_data", QueryToolCalls), ("list_notes", new JsonObject()));
        app.Claude.Text("ok");
        (await AskAsync(session, new AiUiContext(LocalSets: ["Products"]))).ShouldNotContain(e => e.Type == AiStreamEvent.ClientQuery);

        app.Claude.ToolUse(("query_data", QueryToolCalls));
        app.Claude.Text("ok");
        (await AskAsync(session, null)).ShouldNotContain(e => e.Type == AiStreamEvent.ClientQuery);

        await SetAsync(AiSettings.LocalQueries, "false");
        app.Claude.ToolUse(("query_data", QueryToolCalls));
        app.Claude.Text("ok");
        var events = await AskAsync(session, Local);

        events.ShouldNotContain(e => e.Type == AiStreamEvent.ClientQuery);
        (await AuditAsync()).Select(c => c.Channel).ShouldAllBe(c => c == AiChannels.Chat);
    }

    [Fact]
    public async Task Only_the_user_of_the_turn_answers_and_only_with_a_json_object_while_it_waits()
    {
        var other = app.As(await app.UserAsync(Admin, AiPermissions.Chat, AiPermissions.Audit), _setup.TenantId);
        app.Claude.ToolUse(("query_data", QueryToolCalls));
        app.Claude.Text("One.");
        var session = await AiApp.NewSessionAsync(Admin);
        string? callId = null;

        var events = await AskAsync(session, Local, async call =>
        {
            callId = call.CallId;
            (await AnswerAsync(other, session, call.CallId, """{"rows":[{"Tool":"planted"}],"total":1}""")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await AnswerAsync(Admin, session, call.CallId, "not json")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AnswerAsync(Admin, session, call.CallId, new string(' ', AiClientResults.MaxOutputLength + 1))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AnswerAsync(Admin, Guid.NewGuid(), call.CallId, Rows)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await AnswerAsync(Admin, session, call.CallId, Rows)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        });

        events.Single(e => e.Type == AiStreamEvent.Tool).ToolCall!.Output.ShouldBe(Rows);
        ResultTheModelGot().ShouldNotContain("planted");
        // the turn is over: nothing waits any more
        (await AnswerAsync(Admin, session, callId!, Rows)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
