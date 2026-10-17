using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Chat.Tests;

public sealed class ChatConversationTests(ChatApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;
    private UserDto _carol = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await app.CreateUserAsync(_setup, "bob@acme.test", ChatPermissions.Use);
        _carol = await app.CreateUserAsync(_setup, "carol@acme.test", ChatPermissions.Use);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    private HttpClient Carol => app.As(_carol.Id, _setup.TenantId);

    [Fact]
    public async Task Groups_are_named_by_their_members_and_only_the_owner_removes_others()
    {
        var group = await CreateGroupAsync("Lunch", _bob.Id);
        group.Members.Select(m => (m.UserId, m.IsOwner)).ShouldBe([(_setup.AdminUserId, true), (_bob.Id, false)], ignoreOrder: true);
        (await Carol.GetAsync($"/api/v1/chat/chats/{group.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Bob.PutAsJsonAsync($"/api/v1/chat/chats/{group.Id}/name", new RenameChatRequest("Team lunch"), Ct)).EnsureSuccessStatusCode();
        (await Bob.PostAsJsonAsync($"/api/v1/chat/chats/{group.Id}/members", new ChatMembersRequest([_carol.Id]), Ct)).EnsureSuccessStatusCode();
        var seen = (await Carol.GetFromJsonAsync<List<ChatConversationDto>>("/api/v1/chat/chats", Ct))!.ShouldHaveSingleItem();
        (seen.Name, seen.IsGroup, seen.Members.Count).ShouldBe(("Team lunch", true, 3));

        (await Bob.DeleteAsync($"/api/v1/chat/chats/{group.Id}/members/{_carol.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.DeleteAsync($"/api/v1/chat/chats/{group.Id}/members/{_carol.Id}", Ct)).EnsureSuccessStatusCode();
        (await Bob.DeleteAsync($"/api/v1/chat/chats/{group.Id}/members/{_bob.Id}", Ct)).EnsureSuccessStatusCode();

        (await Bob.GetFromJsonAsync<List<ChatConversationDto>>("/api/v1/chat/chats", Ct))!.ShouldBeEmpty();
        (await Bob.PostAsJsonAsync($"/api/v1/chat/chats/{group.Id}/messages", new PostChatMessageRequest("<p>back?</p>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_direct_chat_exists_once_per_pair_and_keeps_its_two_people()
    {
        var first = await OpenDirectAsync(Admin, _bob.Id);
        var second = await OpenDirectAsync(Bob, _setup.AdminUserId);

        second.Id.ShouldBe(first.Id);
        (second.IsGroup, second.Name).ShouldBe((false, "Ada Admin"));
        (await Admin.PostAsJsonAsync($"/api/v1/chat/chats/{first.Id}/members", new ChatMembersRequest([_carol.Id]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsJsonAsync($"/api/v1/chat/chats/{first.Id}/name", new RenameChatRequest("x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Formatting_survives_while_scripts_and_foreign_links_do_not()
    {
        var chat = await OpenDirectAsync(Admin, _bob.Id);

        var message = await PostAsync(Admin, chat.Id, """<p><strong>Hi</strong> <em>there</em><script>alert(1)</script> <a href="javascript:alert(1)">x</a> <a href="https://example.com">site</a></p><ul><li>one</li></ul><img src=x onerror=alert(1)>""");

        message.Text.ShouldBe("Hi there x site\none");
        message.Html.ShouldNotBeNull().ShouldContain("<strong>Hi</strong>");
        message.Html.ShouldContain("<li>one</li>");
        message.Html.ShouldNotContain("script");
        message.Html.ShouldNotContain("javascript");
        message.Html.ShouldNotContain("onerror");
        message.Html.ShouldContain("""href="https://example.com" target="_blank" rel="noopener noreferrer nofollow""");
    }

    [Fact]
    public async Task Only_the_sender_edits_and_deletes_and_the_chat_shows_both()
    {
        var chat = await OpenDirectAsync(Admin, _bob.Id);
        var message = await PostAsync(Admin, chat.Id, "<p>Lunch at 12?</p>");
        message.Source.ShouldBe("<p>Lunch at 12?</p>");

        (await Bob.PutAsJsonAsync($"/api/v1/chat/messages/{message.Id}", new EditChatMessageRequest("<p>hacked</p>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Bob.DeleteAsync($"/api/v1/chat/messages/{message.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Carol.DeleteAsync($"/api/v1/chat/messages/{message.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var edited = await (await Admin.PutAsJsonAsync($"/api/v1/chat/messages/{message.Id}", new EditChatMessageRequest("<p>Lunch at 1?</p>"), Ct))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ChatMessageDto>(Ct);
        (edited!.Text, edited.EditedAt is not null).ShouldBe(("Lunch at 1?", true));
        (await MessagesAsync(Bob, chat.Id)).Single().Source.ShouldBeNull();

        (await Admin.DeleteAsync($"/api/v1/chat/messages/{message.Id}", Ct)).EnsureSuccessStatusCode();

        var deleted = (await MessagesAsync(Bob, chat.Id)).Single();
        (deleted.Deleted, deleted.Text, deleted.Html).ShouldBe((true, "", null));
        (await Admin.PutAsJsonAsync($"/api/v1/chat/messages/{message.Id}", new EditChatMessageRequest("<p>again</p>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reactions_toggle_per_person_and_answers_quote_the_message()
    {
        var chat = await OpenDirectAsync(Admin, _bob.Id);
        var message = await PostAsync(Admin, chat.Id, "<p>Ship it?</p>");

        await ReactAsync(Bob, message.Id, "👍");
        await ReactAsync(Admin, message.Id, "👍");
        var reacted = await ReactAsync(Bob, message.Id, "🎉");
        reacted.Reactions.Single(r => r.Emoji == "👍").UserIds.ShouldBe([_bob.Id, _setup.AdminUserId], ignoreOrder: true);
        (await ReactAsync(Bob, message.Id, "👍")).Reactions.Single(r => r.Emoji == "👍").UserIds.ShouldBe([_setup.AdminUserId]);
        (await Bob.PostAsJsonAsync($"/api/v1/chat/messages/{message.Id}/reactions", new ChatReactionRequest("<b>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Carol.PostAsJsonAsync($"/api/v1/chat/messages/{message.Id}/reactions", new ChatReactionRequest("👍"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var answer = await PostAsync(Bob, chat.Id, "<p>Yes</p>", message.Id);
        answer.ReplyTo.ShouldBe(new ChatReplyDto(message.Id, _setup.AdminUserId, "Ship it?"));
    }

    [Fact]
    public async Task Mentioned_members_get_a_notification_and_people_outside_the_chat_do_not()
    {
        var group = await CreateGroupAsync("Plan", _bob.Id);

        var message = await PostAsync(Admin, group.Id, $"<p>{ChatMentions.Person(_bob.Id, "Bobby")} and {ChatMentions.Person(_carol.Id, "Carol")}, look</p>");

        message.Text.ShouldBe("@Bobby and @Carol, look");
        message.Html!.ShouldContain($"""<a class="cw-mention" href="/people/{_bob.Id}" data-user="{_bob.Id}">@bob@acme.test</a>""");
        var notification = (await app.NotificationsAsync()).ShouldHaveSingleItem();
        (notification.UserId, notification.Type, notification.Link).ShouldBe((_bob.Id, ChatNotificationTypes.Mention, $"/chat?c={group.Id}"));
        notification.Arguments.ShouldBe(["Ada Admin", "@Bobby and @Carol, look"]);
    }

    [Fact]
    public async Task A_mentioned_record_shows_only_to_readers_who_may_see_it()
    {
        var record = Guid.NewGuid();
        TestRecordProvider.Records[record] = (_setup.AdminUserId, "Secret deal");
        var chat = await OpenDirectAsync(Admin, _bob.Id);

        (await Bob.PostAsJsonAsync($"/api/v1/chat/chats/{chat.Id}/messages", new PostChatMessageRequest($"<p>{ChatMentions.Record(record, "Made up")}</p>"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var sent = await PostAsync(Admin, chat.Id, $"<p>See {ChatMentions.Record(record, "Secret deal")}</p>");

        sent.Text.ShouldBe("See #");
        sent.Html!.ShouldContain($"""<a class="cw-mention cw-entity" href="/records/{record}" data-entity="records">#Secret deal</a>""");
        var seen = (await MessagesAsync(Bob, chat.Id)).Single();
        seen.HasEntityMentions.ShouldBeTrue();
        seen.Html.ShouldNotBeNull().ShouldContain("""<span class="cw-mention cw-restricted"></span>""");
        seen.Html.ShouldNotContain("Secret");
        (await Bob.GetFromJsonAsync<ChatMessageDto>($"/api/v1/chat/messages/{sent.Id}", Ct))!.Html!.ShouldNotContain("Secret");
    }

    [Fact]
    public async Task Attachments_reach_the_members_only()
    {
        var chat = await OpenDirectAsync(Admin, _bob.Id);
        var file = await UploadAsync(Admin, chat.Id, "plan.txt", "text/plain", "the plan");
        (file.Name, file.ContentType, file.Size).ShouldBe(("plan.txt", "text/plain", 8L));
        (await Bob.GetAsync($"/api/v1/chat/attachments/{file.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var message = await PostAsync(Admin, chat.Id, "", attachments: [file.Id]);

        message.Text.ShouldBe("📎 plan.txt");
        message.Attachments.ShouldBe([file]);
        (await (await Bob.GetAsync($"/api/v1/chat/attachments/{file.Id}", Ct)).EnsureSuccessStatusCode().Content.ReadAsStringAsync(Ct)).ShouldBe("the plan");
        (await Carol.GetAsync($"/api/v1/chat/attachments/{file.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Carol.PostAsync($"/api/v1/chat/chats/{chat.Id}/attachments?name=x.txt", new StringContent("x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Bob.PostAsJsonAsync($"/api/v1/chat/chats/{chat.Id}/messages", new PostChatMessageRequest("", null, [file.Id]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PostAsync($"/api/v1/chat/chats/{chat.Id}/attachments?name=empty.txt", new StringContent(""), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.DeleteAsync($"/api/v1/chat/messages/{message.Id}", Ct)).EnsureSuccessStatusCode();
        (await Bob.GetAsync($"/api/v1/chat/attachments/{file.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_typing_and_searching_work_per_chat()
    {
        var chat = await OpenDirectAsync(Admin, _bob.Id);
        var (connection, events) = await app.ConnectAsync(_bob.Id, _setup.TenantId);
        await using var hub = connection;
        await PostAsync(Admin, chat.Id, "<p>Budget for March</p>");
        await PostAsync(Admin, chat.Id, "<p>Lunch?</p>");

        (await Bob.GetFromJsonAsync<List<ChatConversationDto>>("/api/v1/chat/chats", Ct))!.Single().Unread.ShouldBe(2);
        (await Bob.GetFromJsonAsync<List<ChatMessageDto>>($"/api/v1/chat/chats/{chat.Id}/messages?search=march%20budget", Ct))!.Single().Text.ShouldBe("Budget for March");
        (await Admin.PostAsync($"/api/v1/chat/chats/{chat.Id}/typing", null, Ct)).EnsureSuccessStatusCode();
        (await ChatTests.NextAsync(events, ChatEvents.Typing)).Deserialize<ChatTypingDto>(JsonSerializerOptions.Web).ShouldBe(new ChatTypingDto(chat.Id, _setup.AdminUserId));

        (await Bob.PostAsync($"/api/v1/chat/chats/{chat.Id}/read", null, Ct)).EnsureSuccessStatusCode();

        (await Bob.GetFromJsonAsync<List<ChatConversationDto>>("/api/v1/chat/chats", Ct))!.Single().Unread.ShouldBe(0);
        (await MessagesAsync(Admin, chat.Id)).ShouldAllBe(m => m.ReadAt != null);
    }

    [Fact]
    public async Task Direct_messages_from_before_conversations_move_into_one()
    {
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
            var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
            var sent = DateTimeOffset.UtcNow.AddDays(-1);
            db.Add(new ChatMessage { TenantId = _setup.TenantId, FromUserId = _bob.Id, ToUserId = _setup.AdminUserId, Text = "old one", SentAt = sent, ReadAt = sent });
            db.Add(new ChatMessage { TenantId = _setup.TenantId, FromUserId = _bob.Id, ToUserId = _setup.AdminUserId, Text = "old two", SentAt = sent.AddMinutes(1) });
            await db.SaveChangesAsync(Ct);
            await ChatLegacyMessages.MoveAsync(db, TimeProvider.System, Ct);
        }

        var chat = (await Admin.GetFromJsonAsync<List<ChatConversationDto>>("/api/v1/chat/chats", Ct))!.ShouldHaveSingleItem();
        (chat.Name, chat.LastMessage, chat.Unread).ShouldBe(("bob@acme.test", "old two", 1));
        (await MessagesAsync(Admin, chat.Id)).Select(m => m.Html).ShouldBe(["<p>old one</p>", "<p>old two</p>"]);
    }

    private async Task<ChatConversationDto> CreateGroupAsync(string name, params Guid[] members) =>
        (await (await Admin.PostAsJsonAsync("/api/v1/chat/chats", new CreateChatGroupRequest(name, members), Ct)).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<ChatConversationDto>(Ct))!;

    private static async Task<ChatConversationDto> OpenDirectAsync(HttpClient client, Guid userId) =>
        (await (await client.PostAsync($"/api/v1/chat/chats/direct/{userId}", null, Ct)).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ChatConversationDto>(Ct))!;

    private static async Task<ChatMessageDto> PostAsync(HttpClient client, Guid chatId, string html, Guid? replyTo = null, IReadOnlyList<Guid>? attachments = null) =>
        (await (await client.PostAsJsonAsync($"/api/v1/chat/chats/{chatId}/messages", new PostChatMessageRequest(html, replyTo, attachments), Ct)).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<ChatMessageDto>(Ct))!;

    private static async Task<List<ChatMessageDto>> MessagesAsync(HttpClient client, Guid chatId) =>
        (await client.GetFromJsonAsync<List<ChatMessageDto>>($"/api/v1/chat/chats/{chatId}/messages", Ct))!;

    private static async Task<ChatMessageDto> ReactAsync(HttpClient client, Guid messageId, string emoji) =>
        (await (await client.PostAsJsonAsync($"/api/v1/chat/messages/{messageId}/reactions", new ChatReactionRequest(emoji), Ct)).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<ChatMessageDto>(Ct))!;

    private static async Task<ChatAttachmentDto> UploadAsync(HttpClient client, Guid chatId, string name, string type, string content)
    {
        var body = new StringContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(type);
        return (await (await client.PostAsync($"/api/v1/chat/chats/{chatId}/attachments?name={name}", body, Ct)).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<ChatAttachmentDto>(Ct))!;
    }
}

/// <summary>Mention spans as the chat editor writes them.</summary>
internal static class ChatMentions
{
    public static string Person(Guid userId, string label) => Span('@', new ChatMentionItem(ChatMentionItem.User, userId.ToString(), label));

    public static string Record(Guid id, string label) => Span('#', new ChatMentionItem(ChatMentionItem.Entity, id.ToString(), label, "records", $"/records/{id}"));

    private static string Span(char denotation, ChatMentionItem item) =>
        $"""<span class="mention" data-__type="{ChatHtml.MentionType}" data-denotation-char="{denotation}" data-id="x1" data-value="{item.Label}" data-__data-json="{WebUtility.HtmlEncode(JsonSerializer.Serialize(item, JsonSerializerOptions.Web))}"><span contenteditable="false">{denotation}{item.Label}</span></span>""";
}
