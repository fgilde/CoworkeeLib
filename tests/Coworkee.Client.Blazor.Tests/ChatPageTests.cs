using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts.Chat;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChatPageTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private readonly IChatApi _api = Substitute.For<IChatApi>();

    public ChatPageTests()
    {
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString())).SetPolicies(Security.PermissionPolicy.For(ChatPermissions.Use));
        _api.GetContactsAsync(default).ReturnsForAnyArgs([new ChatContactDto(Bob, "Bob Builder", "Hi", DateTimeOffset.UtcNow, 1)]);
        _api.GetConversationAsync(Bob, null, default).ReturnsForAnyArgs([Message(Bob, Me, "Hi")]);
        _api.SendAsync(Bob, "Hello Bob", default).ReturnsForAnyArgs(Message(Me, Bob, "Hello Bob"));
    }

    private static ChatMessageDto Message(Guid from, Guid to, string text) => new(Guid.CreateVersion7(), from, to, text, DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task Opens_a_conversation_marks_it_read_sends_and_shows_incoming_messages()
    {
        var page = Render<Pages.Chat>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Bob Builder"));

        await page.Find($"[data-contact='{Bob}']").ClickAsync(new());
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages']").TextContent.ShouldContain("Hi"));
        await _api.Received(1).MarkReadAsync(Bob, Arg.Any<CancellationToken>());

        page.Find("textarea[data-testid='chat-text'], [data-testid='chat-text'] textarea").Input("Hello Bob");
        await page.Find("[data-testid='chat-send']").ClickAsync(new());
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages']").TextContent.ShouldContain("Hello Bob"));

        Services.GetRequiredService<FakeRealtimeConnection>().Push($"user:{Me}", ChatEvents.Message, Message(Bob, Me, "See you"));
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages']").TextContent.ShouldContain("See you"));
    }
}
