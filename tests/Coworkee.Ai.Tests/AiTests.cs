using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Coworkee.Contracts;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
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

    private static ChatRequest Ask(string text) => new([new ChatMessageDto("user", text)]);

    [Fact]
    public async Task Chat_runs_tools_as_the_user_and_audits_them()
    {
        app.Claude.ToolUse(("fail_note", new JsonObject { ["title"] = "bad" }), ("add_note", new JsonObject { ["title"] = "Buy milk" }));
        app.Claude.Text("Added the note Buy milk.");

        var response = await Admin.PostAsJsonAsync("/api/v1/ai/chat", Ask("Note: buy milk"), Ct);
        var answer = (await response.Content.ReadFromJsonAsync<ChatResponseDto>(Ct))!;

        answer.Text.ShouldBe("Added the note Buy milk.");
        answer.ToolCalls.Select(c => (c.Tool, c.Succeeded)).ShouldBe([("fail_note", false), ("add_note", true)]);
        answer.ToolCalls[0].Error!.ShouldContain("not allowed");
        // the failed tool's changes are never saved, the successful one runs as the user
        (await app.InDbAsync(db => db.Set<Note>().Select(n => new { n.Title, n.CreatedBy }).ToListAsync(Ct)))
            .ShouldHaveSingleItem().ShouldBe(new { Title = "Buy milk", CreatedBy = (Guid?)_setup.AdminUserId });

        var requests = app.Claude.Requests.ToList();
        requests.Count.ShouldBe(2);
        requests[0]["model"]!.GetValue<string>().ShouldBe(AiSettings.DefaultModel);
        var addNote = requests[0]["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "add_note")!;
        addNote["input_schema"]!["properties"]!["title"].ShouldNotBeNull();
        addNote["input_schema"]!["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["title"]);
        var results = requests[1]["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        results.Select(r => r!["is_error"]?.GetValue<bool>()).ShouldBe([true, false]);

        var calls = (await Admin.GetFromJsonAsync<PagedResult<AiToolCallDto>>("/api/v1/ai/tool-calls?channel=chat", Ct))!;
        calls.Items.Select(c => c.Tool).ShouldBe(["add_note", "fail_note"], ignoreOrder: true);
        calls.Items.ShouldAllBe(c => c.UserId == _setup.AdminUserId && c.Channel == AiChannels.Chat);
    }

    [Fact]
    public async Task Tools_the_user_lacks_rights_for_are_not_offered_nor_run()
    {
        var chatter = app.As(await UserAsync(AiPermissions.Chat), _setup.TenantId);
        (await chatter.GetFromJsonAsync<AiToolDto[]>("/api/v1/ai/tools", Ct))!.Select(t => t.Name).ShouldBe(["list_notes"]);
        app.Claude.ToolUse(("add_note", new JsonObject { ["title"] = "sneaky" }));
        app.Claude.Text("I could not add it.");

        var answer = (await (await chatter.PostAsJsonAsync("/api/v1/ai/chat", Ask("add sneaky"), Ct)).Content.ReadFromJsonAsync<ChatResponseDto>(Ct))!;

        answer.ToolCalls.Single().Error!.ShouldContain("Unknown tool");
        app.Claude.Requests.First()["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ShouldBe(["list_notes"]);
        (await app.InDbAsync(db => db.Set<Note>().CountAsync(Ct))).ShouldBe(0);
        (await chatter.GetAsync("/api/v1/ai/tool-calls", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var nobody = app.As(await UserAsync(), _setup.TenantId);
        (await nobody.PostAsJsonAsync("/api/v1/ai/chat", Ask("hi"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Chat_rejects_bad_conversations_and_reports_provider_failures()
    {
        (await Admin.PostAsJsonAsync("/api/v1/ai/chat", new ChatRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/ai/chat", new ChatRequest([new("assistant", "hi")]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/ai/chat", new ChatRequest([new("system", "hi")]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // nothing queued: the fake answers with an error
        (await Admin.PostAsJsonAsync("/api/v1/ai/chat", Ask("hi"), Ct)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Mcp_lists_and_calls_the_tools_of_the_signed_in_user()
    {
        var http = Admin;
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http),
            cancellationToken: Ct);

        (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ShouldBe(["add_note", "fail_note", "list_notes"], ignoreOrder: true);
        var added = await client.CallToolAsync("add_note", new Dictionary<string, object?> { ["title"] = "From MCP" }, cancellationToken: Ct);
        added.IsError.ShouldNotBe(true);
        var failed = await client.CallToolAsync("fail_note", new Dictionary<string, object?> { ["title"] = "x" }, cancellationToken: Ct);
        failed.IsError.ShouldBe(true);
        var listed = await client.CallToolAsync("list_notes", cancellationToken: Ct);
        ((ModelContextProtocol.Protocol.TextContentBlock)listed.Content.Single()).Text.ShouldBe("""["From MCP"]""");

        (await app.InDbAsync(db => db.Set<Note>().SingleAsync(Ct))).CreatedBy.ShouldBe(_setup.AdminUserId);
        (await Admin.GetFromJsonAsync<PagedResult<AiToolCallDto>>("/api/v1/ai/tool-calls?channel=mcp", Ct))!.TotalCount.ShouldBe(3);

        (await app.App.GetTestClient().PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> UserAsync(params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", "Some", "One"), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        if (permissions.Length > 0)
        {
            var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }
}
