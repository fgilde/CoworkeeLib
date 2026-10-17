using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.CommandPalette;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Chat;
using Coworkee.Contracts.Chat;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChatDockTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private readonly Guid _chatId;
    private readonly List<string> _stored = [];

    public ChatDockTests()
    {
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString())).SetPolicies(Security.PermissionPolicy.For(ChatPermissions.Use));
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([ChatPermissions.Use]);
        var chat = ChatPageTests.Chat(0);
        _chatId = chat.Id;
        ChatApi.GetChatAsync(_chatId, default).ReturnsForAnyArgs(chat);
        ChatApi.OpenDirectAsync(Bob, default).ReturnsForAnyArgs(chat);
        ChatApi.GetMessagesAsync(_chatId, null, null, default).ReturnsForAnyArgs([ChatPageTests.Message(Bob, "<p>Hi</p>", "Hi")]);
        ChatApi.GetContactsAsync(default).ReturnsForAnyArgs([new ChatContactDto(Bob, "Bob Builder", null, null, 0)]);
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
    }

    private ChatDock Dock => Services.GetRequiredService<ChatDock>();

    private IEnumerable<ChatDockWindow> Stored => JSInterop.Invocations["localStorage.setItem"].Select(i => JsonSerializer.Deserialize<List<ChatDockWindow>>((string)i.Arguments[1]!, JsonSerializerOptions.Web)!).Last();

    [Fact]
    public async Task Windows_open_minimize_with_an_unread_count_close_and_are_kept_for_the_next_visit()
    {
        var dock = Render<ChatDockHost>();
        dock.WaitForAssertion(() => Dock.Available.ShouldBeTrue());

        await dock.InvokeAsync(() => Dock.OpenWithAsync(Bob));
        dock.WaitForAssertion(() => dock.Find($"[data-window='{_chatId}'] [data-testid='chat-messages']").TextContent.ShouldContain("Hi"));
        dock.Find($"[data-window='{_chatId}']").TextContent.ShouldContain("Bob Builder");
        Stored.ShouldBe([new ChatDockWindow(_chatId)]);

        await dock.Find("[data-testid='chat-window-toggle']").ClickAsync(new());
        dock.WaitForAssertion(() => dock.FindAll("[data-testid='chat-messages']").ShouldBeEmpty());
        Stored.ShouldBe([new ChatDockWindow(_chatId, true)]);
        Services.GetRequiredService<FakeRealtimeConnection>().Push($"user:{Me}", ChatEvents.Message, ChatPageTests.Message(Bob, "<p>Still there?</p>", "Still there?"));
        dock.WaitForAssertion(() => dock.Find("[data-testid='chat-window-unread']").TextContent.ShouldBe("1"));

        await dock.Find("[data-testid='chat-window-close']").ClickAsync(new());
        dock.WaitForAssertion(() => dock.FindAll("[data-testid='chat-dock']").ShouldBeEmpty());
        Stored.ShouldBeEmpty();
    }

    [Fact]
    public void A_reload_restores_the_windows_of_the_user()
    {
        JSInterop.Setup<string?>("localStorage.getItem", $"coworkee.chat.dock.{Me:N}")
            .SetResult(JsonSerializer.Serialize(new[] { new ChatDockWindow(_chatId, true) }, JsonSerializerOptions.Web));

        var dock = Render<ChatDockHost>();

        dock.WaitForAssertion(() => dock.Find($"[data-window='{_chatId}']").ClassList.ShouldContain("minimized"));
    }

    [Fact]
    public async Task Without_a_dock_on_screen_a_chat_opens_on_the_chat_page()
    {
        await Dock.OpenAsync(_chatId);

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri.ShouldEndWith($"/chat?c={_chatId}");
    }

    [Fact]
    public async Task The_palette_offers_chat_with_and_people_after_an_at_sign()
    {
        var dock = Render<ChatDockHost>();
        dock.WaitForAssertion(() => Dock.Available.ShouldBeTrue());
        var provider = ActivatorUtilities.CreateInstance<ChatPaletteProvider>(Services);

        (await provider.GetItemsAsync("chat", default)).Select(i => i.Title).ShouldBe(["Chat with…"]);
        var person = (await provider.GetItemsAsync("@bob", default)).ShouldHaveSingleItem();
        person.Title.ShouldBe("Chat with Bob Builder");

        await dock.InvokeAsync(person.Run!);

        await ChatApi.Received(1).OpenDirectAsync(Bob, Arg.Any<CancellationToken>());
        dock.WaitForAssertion(() => dock.Find($"[data-window='{_chatId}']"));
    }

    [Fact]
    public async Task An_avatar_of_someone_else_offers_a_chat()
    {
        Api.GetUserCardsAsync(default!, default).ReturnsForAnyArgs(call => call.Arg<IReadOnlyList<Guid>>().Select(id => new Contracts.Identity.UserCardDto(id, "Bob Builder", null)).ToList());
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var dock = Render<ChatDockHost>();
        dock.WaitForAssertion(() => Dock.Available.ShouldBeTrue());
        var mine = Render<UserAvatar>(p => p.Add(a => a.UserId, Me));
        var bob = Render<UserAvatar>(p => p.Add(a => a.UserId, Bob));

        bob.WaitForAssertion(() => bob.Find("[data-testid='avatar-menu']"));
        mine.FindAll("[data-testid='avatar-menu']").ShouldBeEmpty();
        await bob.InvokeAsync(() => bob.FindComponent<MudBlazor.MudMenu>().Instance.OpenMenuAsync(EventArgs.Empty));
        popovers.WaitForAssertion(() => popovers.Find("[data-testid='avatar-chat']"));
        await popovers.Find("[data-testid='avatar-chat']").ClickAsync(new());

        dock.WaitForAssertion(() => dock.Find($"[data-window='{_chatId}']"));
    }
}
