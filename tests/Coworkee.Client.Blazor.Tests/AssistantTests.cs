using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Ai;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AssistantTests : ClientTestBase
{
    private static readonly Guid Me = Guid.NewGuid();
    private readonly IAssistantApi _api = Substitute.For<IAssistantApi>();

    public AssistantTests()
    {
        Services.AddSingleton(_api);
        Services.AddScoped<AssistantState>();
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([AiPermissions.Chat]);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString())).SetPolicies(Security.PermissionPolicy.For(AiPermissions.Chat));
        _api.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(true));
        _api.GetSessionsAsync(default, default).ReturnsForAnyArgs([]);
    }

    [Fact]
    public async Task The_button_opens_and_closes_the_window_and_stays_away_while_the_assistant_is_off()
    {
        var launcher = Render<AssistantLauncher>();
        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-fab']"));
        launcher.FindAll("[data-testid='assistant-window']").ShouldBeEmpty();

        await launcher.Find("[data-testid='assistant-fab']").ClickAsync(new());
        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-window']"));
        launcher.Find("[data-testid='assistant-empty']").ShouldNotBeNull();
        launcher.Find("[data-testid='assistant-fab']").GetAttribute("aria-expanded").ShouldBe("true");

        await launcher.Find("[data-testid='assistant-size-Full']").ClickAsync(new());
        launcher.Find("[data-testid='assistant-window']").ClassList.ShouldContain("full");
        launcher.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty();
        await launcher.Find("[data-testid='assistant-close']").ClickAsync(new());
        launcher.WaitForAssertion(() => launcher.FindAll("[data-testid='assistant-window']").ShouldBeEmpty());

        // the shortcut toggles it
        await launcher.InvokeAsync(() => launcher.Instance.ToggleFromHotkey());
        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-window']"));

        _api.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(false));
        var off = Render<AssistantLauncher>();
        off.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_streams_in_as_safe_markdown_with_the_tools_it_ran()
    {
        var session = new AiSessionDto(Guid.NewGuid(), "New conversation", DateTimeOffset.Now);
        _api.CreateSessionAsync(default).ReturnsForAnyArgs(session);
        var gate = new TaskCompletionSource();
        var call = new AiToolCallDto(Guid.NewGuid(), "add_edit_product", AiChannels.Chat, "{}", true, null, 5, DateTimeOffset.Now, Me, "\"42\"");
        var answer = new AiMessageDto(Guid.NewGuid(), "assistant", "**Created** <img src=x onerror=alert(1)> [x](javascript:alert(1))", [call], [], null, DateTimeOffset.Now);
        _api.SendAsync(session.Id, "Add a drill", Arg.Any<CancellationToken>())
            .Returns(Stream(gate.Task, new AiStreamEvent(AiStreamEvent.Delta, Text: "**Created**"), new AiStreamEvent(AiStreamEvent.Tool, ToolCall: call),
                new AiStreamEvent(AiStreamEvent.Done, Message: answer)));
        _api.GetSessionAsync(session.Id, default).ReturnsForAnyArgs(new AiSessionDetailDto(session.Id, "Add a drill", DateTimeOffset.Now,
            [new AiMessageDto(Guid.NewGuid(), "user", "Add a drill", [], [], null, DateTimeOffset.Now), answer]));
        Services.GetRequiredService<CoworkeeClientOptions>().AssistantLinks["add_edit_product"] = "/catalog/products/{id}";
        var window = Render<AssistantWindow>();

        window.Find("[data-testid='assistant-input']").Input("Add a drill");
        var sending = window.Find("[data-testid='assistant-send']").ClickAsync(new());

        // the first part is on screen before the answer is complete
        window.WaitForAssertion(() => window.Find("[data-testid='assistant-typing']"));
        window.Find("[data-role='user']").TextContent.ShouldContain("Add a drill");
        gate.SetResult();
        await sending;
        window.WaitForAssertion(() => window.FindAll("[data-testid='assistant-typing']").ShouldBeEmpty());

        var bubble = window.FindAll("[data-testid='assistant-answer']").Last();
        bubble.QuerySelector("b, strong").ShouldNotBeNull(bubble.InnerHtml).TextContent.ShouldBe("Created");
        bubble.TextContent.ShouldContain("<img src=x onerror=alert(1)>");
        bubble.QuerySelectorAll("img").ShouldBeEmpty();
        bubble.QuerySelectorAll("a").ShouldAllBe(a => !(a.GetAttribute("href") ?? string.Empty).StartsWith("javascript", StringComparison.OrdinalIgnoreCase));
        window.Find("[data-tool='add_edit_product']").GetAttribute("href").ShouldBe("/catalog/products/42");
        window.Find("[data-testid='assistant-input']").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_failed_answer_puts_the_question_back()
    {
        var session = new AiSessionDto(Guid.NewGuid(), "Plans", DateTimeOffset.Now);
        _api.CreateSessionAsync(default).ReturnsForAnyArgs(session);
        _api.SendAsync(default, default!, default).ReturnsForAnyArgs(Stream(Task.CompletedTask, new AiStreamEvent(AiStreamEvent.Error, Text: "The provider is down.")));
        Render<MudSnackbarProvider>();
        var window = Render<AssistantWindow>();

        window.Find("[data-testid='assistant-input']").Input("lost?");
        await window.Find("[data-testid='assistant-send']").ClickAsync(new());

        window.WaitForAssertion(() => window.Find("[data-testid='assistant-input']").GetAttribute("value").ShouldBe("lost?"));
        window.FindAll("[data-role='user']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Destructive_tools_wait_for_the_users_decision()
    {
        var session = new AiSessionDto(Guid.NewGuid(), "Cleanup", DateTimeOffset.Now);
        var waiting = new AiMessageDto(Guid.NewGuid(), "assistant", "Shall I?", [], [new AiPendingToolDto("delete_note", """{"title":"old"}""")], null, DateTimeOffset.Now);
        _api.GetSessionsAsync(default, default).ReturnsForAnyArgs([session]);
        _api.GetSessionAsync(session.Id, default).ReturnsForAnyArgs(new AiSessionDetailDto(session.Id, session.Title, session.UpdatedAt,
            [new AiMessageDto(Guid.NewGuid(), "user", "delete old", [], [], null, DateTimeOffset.Now), waiting]));
        _api.ConfirmAsync(session.Id, true, Arg.Any<CancellationToken>())
            .Returns(Stream(Task.CompletedTask, new AiStreamEvent(AiStreamEvent.Done, Message: new AiMessageDto(Guid.NewGuid(), "assistant", "Deleted.", [], [], null, DateTimeOffset.Now))));
        var window = Render<AssistantWindow>();
        window.WaitForAssertion(() => window.Find("[data-testid='assistant-confirm']").TextContent.ShouldContain("delete_note"));
        window.Find("[data-testid='assistant-confirm'] [data-destructive='true']").ShouldNotBeNull();

        _api.GetSessionAsync(session.Id, default).ReturnsForAnyArgs(new AiSessionDetailDto(session.Id, session.Title, session.UpdatedAt,
            [waiting with { Decision = AiDecisions.Approved }, new AiMessageDto(Guid.NewGuid(), "assistant", "Deleted.", [], [], null, DateTimeOffset.Now)]));
        await window.Find("[data-testid='assistant-approve']").ClickAsync(new());

        window.WaitForAssertion(() => window.Markup.ShouldContain("Deleted."));
        window.FindAll("[data-testid='assistant-approve']").ShouldBeEmpty();
        _api.Received(1).ConfirmAsync(session.Id, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sessions_can_be_searched_renamed_and_deleted_and_follow_other_tabs()
    {
        var party = new AiSessionDto(Guid.NewGuid(), "Summer party", DateTimeOffset.Now);
        var budget = new AiSessionDto(Guid.NewGuid(), "Budget", DateTimeOffset.Now.AddHours(-1));
        _api.GetSessionsAsync(default, default).ReturnsForAnyArgs([party, budget]);
        _api.GetSessionAsync(default, default).ReturnsForAnyArgs(call => new AiSessionDetailDto(call.Arg<Guid>(), "x", DateTimeOffset.Now, []));
        var dialogs = Render<MudDialogProvider>();
        Services.GetRequiredService<AssistantState>().Open(AssistantSize.Large);
        var window = Render<AssistantWindow>();
        window.WaitForAssertion(() => window.FindAll("[data-session]").Count.ShouldBe(2));

        window.Find("input[data-testid='assistant-search'], [data-testid='assistant-search'] input").Input("party");
        window.WaitForAssertion(() => _api.Received().GetSessionsAsync("party", Arg.Any<CancellationToken>()));

        await window.InvokeAsync(() => window.FindAll("[data-testid='assistant-rename']")[1].Click());
        window.Find("input[data-testid='assistant-rename-input'], [data-testid='assistant-rename-input'] input").Change("Budget 2027");
        await window.InvokeAsync(() => window.Find("[data-testid='assistant-rename-save']").Click());
        await _api.Received(1).RenameSessionAsync(budget.Id, "Budget 2027", Arg.Any<CancellationToken>());

        var deleting = window.FindAll("[data-testid='assistant-delete']")[0].ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Delete").ShouldBeTrue());
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;
        await _api.Received(1).DeleteSessionAsync(party.Id, Arg.Any<CancellationToken>());

        // another tab added a conversation
        _api.ClearReceivedCalls();
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(Me), RealtimeEventTypes.EntityChanged,
            new EntityChangedPayload("AiChatSession", Guid.NewGuid().ToString(), EntityChangedPayload.Created, []));
        window.WaitForAssertion(() => _api.ReceivedWithAnyArgs().GetSessionsAsync(default, default));
    }

    private static async IAsyncEnumerable<AiStreamEvent> Stream(Task gate, AiStreamEvent first, params AiStreamEvent[] rest)
    {
        yield return first;
        await gate;
        foreach (var item in rest)
        {
            yield return item;
        }
    }
}
