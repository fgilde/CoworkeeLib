using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Nodes;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;

namespace Coworkee.Ai.Tests;

public sealed class AiTests(AiApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static AiMessageDto Done(IReadOnlyList<AiStreamEvent> events) => events[^1].ShouldNotBeNull().Message.ShouldNotBeNull();

    [Fact]
    public async Task Chat_streams_runs_tools_as_the_user_audits_them_and_keeps_the_conversation()
    {
        app.Claude.ToolUse(("list_notes", new JsonObject()), ("add_note", new JsonObject { ["title"] = "Buy milk" }));
        var session = await AiApp.NewSessionAsync(Admin);

        var waiting = Done(await AiApp.AskAsync(Admin, session, "Note: buy milk"));

        // reading runs at once, changing data waits for the user's click
        waiting.ToolCalls.Select(c => c.Tool).ShouldBe(["list_notes"]);
        waiting.Pending.ShouldHaveSingleItem().ShouldBe(new AiPendingToolDto("add_note", """{"title":"Buy milk"}""", true));
        (await app.InDbAsync(db => db.Set<Note>().CountAsync(Ct))).ShouldBe(0);

        app.Claude.Text("Added the note Buy milk.");
        var events = await AiApp.StreamAsync(Admin, session, new AiConfirmRequest(true), "confirm");

        events.Where(e => e.Type == AiStreamEvent.Delta).Select(e => e.Text).ShouldBe(["Added the no", "te Buy milk."]);
        events.Where(e => e.Type == AiStreamEvent.Tool).Select(e => e.ToolCall!.Tool).ShouldBe(["add_note"]);
        Done(events).Text.ShouldBe("Added the note Buy milk.");
        (await app.InDbAsync(db => db.Set<Note>().Select(n => new { n.Title, n.CreatedBy }).ToListAsync(Ct)))
            .ShouldHaveSingleItem().ShouldBe(new { Title = "Buy milk", CreatedBy = (Guid?)_setup.AdminUserId });

        var requests = app.Claude.Requests.ToList();
        requests.Count.ShouldBe(2);
        requests[0]["stream"]!.GetValue<bool>().ShouldBeTrue();
        requests[0]["model"]!.GetValue<string>().ShouldBe(AiSettings.DefaultModel);
        var addNote = requests[0]["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "add_note")!;
        addNote["input_schema"]!["properties"]!["title"].ShouldNotBeNull();
        addNote["input_schema"]!["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["title"]);
        addNote["description"]!.GetValue<string>().ShouldContain("confirmed");
        var results = requests[1]["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        results.Select(r => r!["is_error"]?.GetValue<bool>()).ShouldBe([false, false]);

        var calls = (await Admin.GetFromJsonAsync<PagedResult<AiToolCallDto>>("/api/v1/ai/tool-calls?channel=chat", Ct))!;
        calls.Items.Select(c => c.Tool).ShouldBe(["add_note", "list_notes"], ignoreOrder: true);
        calls.Items.ShouldAllBe(c => c.UserId == _setup.AdminUserId && c.Channel == AiChannels.Chat && c.ApiKeyId == null);

        var saved = (await Admin.GetFromJsonAsync<AiSessionDetailDto>($"/api/v1/ai/sessions/{session}", Ct))!;
        saved.Title.ShouldBe("Note: buy milk");
        saved.Messages.Select(m => (m.Role, m.Text)).ShouldBe([("user", "Note: buy milk"), ("assistant", ""), ("assistant", "Added the note Buy milk.")]);
        saved.Messages[1].Decision.ShouldBe(AiDecisions.Approved);

        // the next question carries the conversation
        app.Claude.Text("You asked to buy milk.");
        Done(await AiApp.AskAsync(Admin, session, "What did I ask?")).Text.ShouldBe("You asked to buy milk.");
        app.Claude.Requests.Last()["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()).ShouldBe(["user", "assistant", "user"]);
    }

    [Fact]
    public async Task Planted_text_never_changes_data_without_the_users_click()
    {
        // text planted in a note, then in the conversation, must not make the assistant change data on its own
        var session = await AiApp.NewSessionAsync(Admin);
        app.Claude.ToolUse(("list_notes", new JsonObject()));
        app.Claude.ToolUse(("add_note", new JsonObject { ["title"] = "planted" }));

        Done(await AiApp.AskAsync(Admin, session, "look at my notes")).Pending.Single().Tool.ShouldBe("add_note");
        app.Claude.ToolUse(("add_note", new JsonObject { ["title"] = "planted again" }));
        Done(await AiApp.AskAsync(Admin, session, "thanks")).Pending.Single().Tool.ShouldBe("add_note");

        (await app.InDbAsync(db => db.Set<Note>().CountAsync(Ct))).ShouldBe(0);
        // a new question declined what still waited
        (await Admin.GetFromJsonAsync<AiSessionDetailDto>($"/api/v1/ai/sessions/{session}", Ct))!.Messages
            .Where(m => m.Pending.Count > 0).Select(m => m.Decision).ShouldBe([AiDecisions.Declined, null]);
    }

    [Fact]
    public async Task Destructive_tools_run_only_after_the_user_confirmed_them()
    {
        await app.InDbAsync(async db =>
        {
            db.AddRange(new Note { Title = "old" }, new Note { Title = "older" });
            return await db.SaveChangesAsync(Ct);
        });
        var session = await AiApp.NewSessionAsync(Admin);
        app.Claude.ToolUse(("delete_note", new JsonObject { ["title"] = "old" }), ("list_notes", new JsonObject()));

        var waiting = Done(await AiApp.AskAsync(Admin, session, "delete old"));

        waiting.Pending.ShouldHaveSingleItem().ShouldBe(new AiPendingToolDto("delete_note", """{"title":"old"}""", true));
        waiting.Decision.ShouldBeNull();
        waiting.ToolCalls.Select(c => c.Tool).ShouldBe(["list_notes"]);
        (await app.InDbAsync(db => db.Set<Note>().CountAsync(Ct))).ShouldBe(2);

        // the arguments wait encrypted; only the masked display copy is readable
        var stored = await app.InDbAsync(db => db.Set<Sessions.AiChatMessage>().Where(m => m.Pending != null).Select(m => m.Pending!).SingleAsync(Ct));
        Sessions.AiPendingProtector.Read(stored).Calls.ShouldNotContain("old");

        // two confirmations at once: one decides and runs, the call never runs twice
        app.Claude.Text("Deleted old.");
        var first = Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/confirm", new AiConfirmRequest(true), Ct);
        var second = Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/confirm", new AiConfirmRequest(true), Ct);
        var bodies = await Task.WhenAll((await Task.WhenAll(first, second)).Select(r => r.Content.ReadAsStringAsync(Ct)));

        bodies.Count(b => b.Contains("Deleted old.")).ShouldBe(1);
        (await app.InDbAsync(db => db.Set<Note>().Select(n => n.Title).ToListAsync(Ct))).ShouldBe(["older"]);
        (await app.InDbAsync(db => db.Set<AiToolCall>().CountAsync(c => c.Tool == "delete_note", Ct))).ShouldBe(1);
        (await Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/confirm", new AiConfirmRequest(true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.GetFromJsonAsync<AiSessionDetailDto>($"/api/v1/ai/sessions/{session}", Ct))!.Messages[1].Decision.ShouldBe(AiDecisions.Approved);

        app.Claude.ToolUse(("delete_note", new JsonObject { ["title"] = "older" }));
        Done(await AiApp.AskAsync(Admin, session, "delete older too")).Pending.Count.ShouldBe(1);
        app.Claude.Text("Kept it.");
        await AiApp.StreamAsync(Admin, session, new AiConfirmRequest(false), "confirm");
        (await app.InDbAsync(db => db.Set<Note>().CountAsync(Ct))).ShouldBe(1);
        app.Claude.Requests.Last()["messages"]!.AsArray()[^1]!["content"]!.AsArray().Single()!["is_error"]!.GetValue<bool>().ShouldBeTrue();
        (await Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/confirm", new AiConfirmRequest(true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cut_off_turns_run_no_tools_and_provider_failures_keep_the_done_calls()
    {
        var session = await AiApp.NewSessionAsync(Admin);
        app.Claude.ToolUseStopping("max_tokens", ("list_notes", new JsonObject()));
        Done(await AiApp.AskAsync(Admin, session, "list half")).ToolCalls.ShouldBeEmpty();

        // the second round fails: the done call is listed and the answer says so
        app.Claude.ToolUse(("list_notes", new JsonObject()));
        var answer = Done(await AiApp.AskAsync(Admin, session, "list them"));
        answer.Text.ShouldContain("already carried out");
        answer.ToolCalls.Single().Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Audit_masks_secret_looking_inputs()
    {
        var input = System.Text.Json.JsonDocument.Parse("""{"email":"a@b.c","password":"x","nested":{"apiKey":"k"},"list":[{"token":"t"}]}""").RootElement;
        AiAuditRedaction.Redact(input).ShouldBe("""{"email":"a@b.c","password":"***","nested":{"apiKey":"***"},"list":[{"token":"***"}]}""");
    }

    [Fact]
    public async Task Tools_the_user_lacks_rights_for_are_not_offered_nor_run()
    {
        var chatter = app.As(await UserAsync(AiPermissions.Chat), _setup.TenantId);
        (await chatter.GetFromJsonAsync<AiToolDto[]>("/api/v1/ai/tools", Ct))!.Select(t => t.Name).ShouldBe(["list_notes", "count_notes", "pin_notes", "list_data_sets", "query_data"], ignoreOrder: true);
        app.Claude.ToolUse(("add_note", new JsonObject { ["title"] = "sneaky" }));
        app.Claude.Text("I could not add it.");

        var answer = Done(await AiApp.AskAsync(chatter, await AiApp.NewSessionAsync(chatter), "add sneaky"));

        answer.ToolCalls.Single().Error!.ShouldContain("Unknown tool");
        app.Claude.Requests.First()["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>())
            .ShouldBe(["list_notes", "count_notes", "pin_notes", "list_data_sets", "query_data"], ignoreOrder: true);
        (await app.InDbAsync(db => db.Set<Note>().CountAsync(Ct))).ShouldBe(0);
        (await chatter.GetAsync("/api/v1/ai/tool-calls", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await chatter.GetFromJsonAsync<AiStatusDto>("/api/v1/ai/status", Ct))!.Available.ShouldBeTrue();

        var nobody = app.As(await UserAsync(), _setup.TenantId);
        (await nobody.PostAsJsonAsync("/api/v1/ai/sessions", new AiSessionRequest(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetFromJsonAsync<AiStatusDto>("/api/v1/ai/status", Ct))!.Available.ShouldBeFalse();
    }

    [Fact]
    public async Task Chat_rejects_bad_messages_and_reports_provider_failures()
    {
        var session = await AiApp.NewSessionAsync(Admin);
        (await Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/messages", new AiSendRequest(" "), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{session}/messages", new AiSendRequest(new string('x', AiChat.MaxMessageLength + 1)), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync($"/api/v1/ai/sessions/{Guid.NewGuid()}/messages", new AiSendRequest("hi"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // nothing queued: the fake answers with an error, and the question is not kept, so it can be sent again
        var events = await AiApp.AskAsync(Admin, session, "hi");
        events.Single().Type.ShouldBe(AiStreamEvent.Error);
        (await Admin.GetFromJsonAsync<AiSessionDetailDto>($"/api/v1/ai/sessions/{session}", Ct))!.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sessions_belong_to_their_user()
    {
        var mine = await AiApp.NewSessionAsync(Admin);
        app.Claude.Text("Hello.");
        await AiApp.AskAsync(Admin, mine, "Plan the summer party");
        var other = await AiApp.NewSessionAsync(Admin);
        (await Admin.PutAsJsonAsync($"/api/v1/ai/sessions/{other}", new AiSessionRequest("Budget"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetFromJsonAsync<AiSessionDto[]>("/api/v1/ai/sessions", Ct))!.Select(s => s.Title).ShouldBe(["Budget", "Plan the summer party"]);
        (await Admin.GetFromJsonAsync<AiSessionDto[]>("/api/v1/ai/sessions?search=summer", Ct))!.Single().Id.ShouldBe(mine);
        (await Admin.GetFromJsonAsync<AiSessionDto[]>("/api/v1/ai/sessions?search=hello", Ct))!.Single().Id.ShouldBe(mine);

        var stranger = app.As(await UserAsync(AiPermissions.Chat), _setup.TenantId);
        (await stranger.GetFromJsonAsync<AiSessionDto[]>("/api/v1/ai/sessions", Ct))!.ShouldBeEmpty();
        (await stranger.GetAsync($"/api/v1/ai/sessions/{mine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PutAsJsonAsync($"/api/v1/ai/sessions/{mine}", new AiSessionRequest("Mine now"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"/api/v1/ai/sessions/{mine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/v1/ai/sessions/{mine}/messages", new AiSendRequest("hi"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Admin.DeleteAsync($"/api/v1/ai/sessions/{mine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<AiSessionDto[]>("/api/v1/ai/sessions", Ct))!.Select(s => s.Id).ShouldBe([other]);
        (await app.InDbAsync(db => db.Set<Sessions.AiChatMessage>().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Mcp_lists_and_calls_the_tools_of_the_signed_in_user()
    {
        var http = Admin;
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http),
            cancellationToken: Ct);

        var listed = await client.ListToolsAsync(cancellationToken: Ct);
        listed.Select(t => t.Name).ShouldContain("add_note");
        listed.Select(t => t.Name).ShouldContain("get_users");
        listed.Select(t => t.Name).ShouldNotContain("set_grants");
        listed.Select(t => t.Name).ShouldNotContain("create_api_key");
        listed.Single(t => t.Name == "delete_note").ProtocolTool.Annotations!.DestructiveHint.ShouldBe(true);
        listed.Single(t => t.Name == "add_note").ProtocolTool.Annotations!.ShouldSatisfyAllConditions(a => a.DestructiveHint.ShouldBe(true), a => a.ReadOnlyHint.ShouldBe(false));
        listed.Single(t => t.Name == "list_notes").ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(true);
        var added = await client.CallToolAsync("add_note", new Dictionary<string, object?> { ["title"] = "From MCP" }, cancellationToken: Ct);
        added.IsError.ShouldNotBe(true);
        var failed = await client.CallToolAsync("fail_note", new Dictionary<string, object?> { ["title"] = "x" }, cancellationToken: Ct);
        failed.IsError.ShouldBe(true);
        var notes = await client.CallToolAsync("list_notes", cancellationToken: Ct);
        ((ModelContextProtocol.Protocol.TextContentBlock)notes.Content.Single()).Text.ShouldBe("""["From MCP"]""");

        (await app.InDbAsync(db => db.Set<Note>().SingleAsync(Ct))).CreatedBy.ShouldBe(_setup.AdminUserId);
        (await Admin.GetFromJsonAsync<PagedResult<AiToolCallDto>>("/api/v1/ai/tool-calls?channel=mcp", Ct))!.TotalCount.ShouldBe(3);

        (await app.App.GetTestClient().PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var plain = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "text/plain");
        (await Admin.PostAsync("/mcp", plain, Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.UnsupportedMediaType);
        var foreign = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }) };
        foreign.Headers.Add("Origin", "https://evil.test");
        (await Admin.SendAsync(foreign, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // a key header alone is no key sign-in
        var pretending = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }) };
        pretending.Headers.Add("Origin", "https://evil.test");
        pretending.Headers.TryAddWithoutValidation(ApiKeyDefaults.Header, " ");
        (await Admin.SendAsync(pretending, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // the administrators' kill switch: nothing listed, nothing run
        (await Admin.PutAsJsonAsync("/api/v1/settings/global", new Contracts.Settings.SetSettingsRequest(new Dictionary<string, string?> { [AiSettings.McpEnabled] = "false" }), Ct))
            .EnsureSuccessStatusCode();
        (await client.ListToolsAsync(cancellationToken: Ct)).ShouldBeEmpty();
        (await client.CallToolAsync("list_notes", cancellationToken: Ct)).IsError.ShouldBe(true);
        (await Admin.PutAsJsonAsync("/api/v1/settings/global", new Contracts.Settings.SetSettingsRequest(new Dictionary<string, string?> { [AiSettings.McpEnabled] = "true" }), Ct))
            .EnsureSuccessStatusCode();
        (await client.ListToolsAsync(cancellationToken: Ct)).ShouldNotBeEmpty();

        // without the assistant permission MCP offers and runs nothing, even tools the user could otherwise use
        var writer = app.As(await UserAsync(NotePermissions.Write), _setup.TenantId);
        await using var denied = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(writer.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, writer),
            cancellationToken: Ct);
        (await denied.ListToolsAsync(cancellationToken: Ct)).ShouldBeEmpty();
        (await denied.CallToolAsync("add_note", new Dictionary<string, object?> { ["title"] = "no" }, cancellationToken: Ct)).IsError.ShouldBe(true);
        (await writer.GetFromJsonAsync<AiToolDto[]>("/api/v1/ai/tools", Ct))!.ShouldBeEmpty();
    }

    private Task<Guid> UserAsync(params string[] permissions) => app.UserAsync(Admin, permissions);
}
