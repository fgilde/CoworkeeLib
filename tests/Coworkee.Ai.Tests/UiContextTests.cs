using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Ai.Tests;

public sealed class UiContextTests(AiApp app) : IAsyncLifetime
{
    private static readonly JsonElement ModeSchema = JsonDocument.Parse("""
        {"type":"object","properties":{"mode":{"type":"string","enum":["light","dark","system"]}},"required":["mode"]}
        """).RootElement;

    private static readonly AiUiContext Ui = new(
        Pages: [new AiUiPage("Themes", "/admin/themes", "Administration > System", "Colors, fonts and logo of the app."), new AiUiPage("Users", "/admin/users", "Administration > Identity")],
        Current: new AiUiPage("Users", "/admin/users"),
        Themes: ["Default", "Ocean"], Theme: "Default", Mode: "light", Languages: ["en", "de"], Language: "en",
        Actions: [new AiClientActionDto("set_color_mode", "Switches between light, dark and system mode.", ModeSchema)]);

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static string SystemOf(JsonObject request) => request["system"] switch
    {
        JsonArray blocks => string.Join("\n", blocks.Select(b => b!["text"]!.GetValue<string>())),
        { } text => text.GetValue<string>(),
        _ => string.Empty,
    };

    private static IEnumerable<string> ToolsOf(JsonObject request) => request["tools"]?.AsArray().Select(t => t!["name"]!.GetValue<string>()) ?? [];

    [Fact]
    public async Task The_system_prompt_carries_the_ui_the_user_sees()
    {
        app.Claude.Text("Open Administration > System > Themes.");

        await AiApp.StreamAsync(Admin, await AiApp.NewSessionAsync(Admin), new AiSendRequest("How do I change the colors?", Ui));

        var system = SystemOf(app.Claude.Requests.Single());
        system.ShouldContain("Administration > System > Themes (/admin/themes): Colors, fonts and logo of the app.");
        system.ShouldContain("Administration > Identity > Users (/admin/users)");
        system.ShouldContain("Current page: Users (/admin/users)");
        system.ShouldContain("Ocean");
        system.ShouldContain("click path");
    }

    [Fact]
    public async Task The_ui_context_stays_within_its_budget()
    {
        var pages = Enumerable.Range(0, 300).Select(i => new AiUiPage($"Page {i}", $"/pages/{i}", "Group", new string('d', 1_000))).ToList();
        app.Claude.Text("ok");

        await AiApp.StreamAsync(Admin, await AiApp.NewSessionAsync(Admin), new AiSendRequest("hi", new AiUiContext(pages)));

        var system = SystemOf(app.Claude.Requests.Single());
        system.Length.ShouldBeLessThan(7_000);
        system.ShouldContain("Group > Page 0 (/pages/0)");
        system.ShouldNotContain(new string('d', 300));
    }

    [Fact]
    public async Task Client_actions_are_offered_only_when_the_client_declares_them()
    {
        var session = await AiApp.NewSessionAsync(Admin);
        app.Claude.Text("ok");
        await AiApp.AskAsync(Admin, session, "hi");
        ToolsOf(app.Claude.Requests.Last()).ShouldNotContain("set_color_mode");
        SystemOf(app.Claude.Requests.Last()).ShouldNotContain("Current page");

        app.Claude.Text("ok");
        await AiApp.StreamAsync(Admin, session, new AiSendRequest("hi", Ui));
        var tool = app.Claude.Requests.Last()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "set_color_mode")!;
        tool["input_schema"]!["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["mode"]);

        // a client action can neither take the name of a server tool nor carry a bad name
        app.Claude.Text("ok");
        await AiApp.StreamAsync(Admin, session, new AiSendRequest("hi", new AiUiContext(Actions:
            [new AiClientActionDto("add_note", "Fake"), new AiClientActionDto("bad name!", "Bad")])));
        var tools = ToolsOf(app.Claude.Requests.Last()).ToList();
        tools.Count(t => t == "add_note").ShouldBe(1);
        tools.ShouldNotContain("bad name!");
        app.Claude.Requests.Last()["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "add_note")!["description"]!.GetValue<string>().ShouldNotBe("Fake");
    }

    [Fact]
    public async Task A_client_action_streams_to_the_client_and_runs_nothing_on_the_server()
    {
        app.Claude.ToolUse(("set_color_mode", new JsonObject { ["mode"] = "dark" }));
        app.Claude.Text("Switched to dark mode.");

        var events = await AiApp.StreamAsync(Admin, await AiApp.NewSessionAsync(Admin), new AiSendRequest("dark please", Ui));

        var action = events.Single(e => e.Type == AiStreamEvent.ClientAction).Action.ShouldNotBeNull();
        action.Name.ShouldBe("set_color_mode");
        JsonNode.Parse(action.Arguments)!["mode"]!.GetValue<string>().ShouldBe("dark");
        action.CallId.ShouldNotBeNullOrEmpty();
        events.ShouldNotContain(e => e.Type == AiStreamEvent.Tool);
        events[^1].Message!.Text.ShouldBe("Switched to dark mode.");
        (await app.InDbAsync(db => db.Set<AiToolCall>().CountAsync(Ct))).ShouldBe(0);
        (await Admin.GetFromJsonAsync<PagedResult<AiToolCallDto>>("/api/v1/ai/tool-calls", Ct))!.TotalCount.ShouldBe(0);

        // the model goes on with a result that says the client was asked
        var result = app.Claude.Requests.Last()["messages"]!.AsArray()[^1]!["content"]!.AsArray().Single()!;
        result["is_error"]?.GetValue<bool>().ShouldNotBe(true);
        result["content"]!.ToJsonString().ShouldContain("client");
    }

    [Fact]
    public async Task Undeclared_client_actions_do_not_reach_the_client()
    {
        app.Claude.ToolUse(("set_color_mode", new JsonObject { ["mode"] = "dark" }));
        app.Claude.Text("I cannot.");

        var events = await AiApp.AskAsync(Admin, await AiApp.NewSessionAsync(Admin), "dark please");

        events.ShouldNotContain(e => e.Type == AiStreamEvent.ClientAction);
        events[^1].Message!.ToolCalls.Single().Error!.ShouldContain("Unknown tool");
    }
}
