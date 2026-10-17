using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Contracts.Chat;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChatPageTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private static readonly Guid ChatId = Guid.CreateVersion7();
    private readonly IChatApi _api;

    public ChatPageTests()
    {
        _api = ChatApi;
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString())).SetPolicies(Security.PermissionPolicy.For(ChatPermissions.Use));
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([ChatPermissions.Use]);
        _api.GetChatsAsync(default).ReturnsForAnyArgs([Chat(1)]);
        _api.GetChatAsync(ChatId, default).ReturnsForAnyArgs(Chat(0));
        _api.GetMessagesAsync(ChatId, null, null, default).ReturnsForAnyArgs([Message(Bob, "<p>Hi</p>", "Hi")]);
        _api.PostAsync(ChatId, Arg.Any<PostChatMessageRequest>(), default).ReturnsForAnyArgs(Message(Me, "<p>Hello <strong>Bob</strong></p>", "Hello Bob"));
    }

    internal static ChatConversationDto Chat(int unread) =>
        new(ChatId, false, "Bob Builder", [new ChatMemberDto(Me, "Ada", null, false), new ChatMemberDto(Bob, "Bob Builder", DateTimeOffset.UtcNow, false)], "Hi", DateTimeOffset.UtcNow, unread);

    internal static ChatMessageDto Message(Guid from, string html, string text) =>
        new(Guid.CreateVersion7(), from, from == Me ? Bob : Me, text, DateTimeOffset.UtcNow.AddMinutes(-1), null) { ConversationId = ChatId, Html = html };

    [Fact]
    public async Task Opens_a_chat_marks_it_read_sends_formatted_text_and_shows_incoming_messages()
    {
        var page = Render<Pages.Chat>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Bob Builder"));

        await page.Find($"[data-conversation='{ChatId}']").ClickAsync(new());
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages']").TextContent.ShouldContain("Hi"));
        await _api.Received().MarkChatReadAsync(ChatId, Arg.Any<CancellationToken>());

        var editor = page.FindComponent<RichTextEditor>();
        await page.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("<p>Hello <strong>Bob</strong></p>"));
        await page.Find("[data-testid='chat-send']").ClickAsync(new());

        await _api.Received(1).PostAsync(ChatId, Arg.Is<PostChatMessageRequest>(r => r.Html == "<p>Hello <strong>Bob</strong></p>" && r.AttachmentIds!.Count == 0), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages'] strong").TextContent.ShouldBe("Bob"));
        await _api.Received().TypingAsync(ChatId, Arg.Any<CancellationToken>());

        Services.GetRequiredService<FakeRealtimeConnection>().Push($"user:{Me}", ChatEvents.Message, Message(Bob, "<p>See you</p>", "See you"));
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages']").TextContent.ShouldContain("See you"));
    }

    [Fact]
    public void Messages_show_mentions_records_the_reader_may_not_see_attachments_reactions_and_edits()
    {
        var restricted = Message(Bob, $"""<p><a class="cw-mention" href="/people/{Me}" data-user="{Me}">@Ada</a> see {ChatMarkup.Restricted}</p>""", "@Ada see #") with
        {
            EditedAt = DateTimeOffset.UtcNow,
            Attachments = [new ChatAttachmentDto(Guid.NewGuid(), "plan.pdf", "application/pdf", 2048), new ChatAttachmentDto(Guid.NewGuid(), "shot.png", "image/png", 10)],
            Reactions = [new ChatReactionDto("👍", [Me, Bob])],
        };
        _api.GetMessagesAsync(ChatId, null, null, default).ReturnsForAnyArgs([restricted]);

        var thread = Render<Components.Chat.ChatThread>(p => p.Add(t => t.ConversationId, ChatId));

        thread.WaitForAssertion(() => thread.Find("a.cw-mention").GetAttribute("href").ShouldBe($"/people/{Me}"));
        thread.Find(".cw-restricted").TextContent.ShouldBe("#restricted");
        thread.Find("[data-file='plan.pdf']").TextContent.ShouldContain("2 KB");
        thread.Find(".cw-chat-image img").GetAttribute("alt").ShouldBe("shot.png");
        thread.Find("[data-reaction='👍']").ClassList.ShouldContain("own");
        thread.Find("[data-testid='chat-edited']");
    }

    [Fact]
    public async Task Attachments_show_the_small_picture_and_open_in_the_file_viewer()
    {
        var picture = new ChatAttachmentDto(Guid.NewGuid(), "shot.png", "image/png", 10) { HasThumbnail = true };
        var document = new ChatAttachmentDto(Guid.NewGuid(), "plan.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 2048);
        _api.GetMessagesAsync(ChatId, null, null, default).ReturnsForAnyArgs([Message(Bob, "<p>Look</p>", "Look") with { Attachments = [picture, document] }]);
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudPopoverProvider>();
        var thread = Render<Components.Chat.ChatThread>(p => p.Add(t => t.ConversationId, ChatId));

        thread.WaitForAssertion(() => thread.Find(".cw-chat-image img").GetAttribute("src").ShouldBe($"api/v1/chat/attachments/{picture.Id}/thumbnail"));
        await thread.Find("[data-file='shot.png']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.FindComponent<MudBlazor.Extensions.Components.MudExFileDisplay>().Instance.Url
            .ShouldBe($"http://localhost/api/v1/chat/attachments/{picture.Id}"));

        await thread.Find("[data-file='plan.docx']").ClickAsync(new());
        dialogs.WaitForAssertion(() =>
        {
            var display = dialogs.FindComponents<MudBlazor.Extensions.Components.MudExFileDisplay>().Single(d => d.Instance.FileName == "plan.docx").Instance;
            display.Url.ShouldBe($"http://localhost/api/v1/chat/attachments/{document.Id}");
            display.IgnoredRenderControls.ShouldContain(typeof(MudBlazor.Extensions.Components.MudExFileDisplayOfficeLive));
        });
    }

    [Fact]
    public async Task People_and_records_are_suggested_as_mentions()
    {
        _api.GetContactsAsync(default).ReturnsForAnyArgs([new ChatContactDto(Bob, "Bob Builder", null, null, 0), new ChatContactDto(Guid.NewGuid(), "Carol", null, null, 0)]);
        var search = new FakeGlobalSearchApi();
        var source = new ChatMentionSource(_api, search);

        (await source.SearchAsync('@', "bo")).ShouldBe([new ChatMentionItem(ChatMentionItem.User, Bob.ToString(), "Bob Builder")]);

        var record = Guid.NewGuid();
        var records = source.SearchAsync('#', "bolt");
        search.Push(new Contracts.Search.GlobalSearchHit("products", "Products", "Bolt M8") { Id = record.ToString(), Url = $"/products/{record}", Icon = "Inventory" });
        search.Push(new Contracts.Search.GlobalSearchHit("chats", "Chat", "Bob Builder") { Id = ChatId.ToString() });
        search.Complete();
        (await records).ShouldBe([new ChatMentionItem(ChatMentionItem.Entity, record.ToString(), "Bolt M8", "products", $"/products/{record}", "Inventory")]);
        (await source.SearchAsync('#', "b")).ShouldBeEmpty();
    }
}
