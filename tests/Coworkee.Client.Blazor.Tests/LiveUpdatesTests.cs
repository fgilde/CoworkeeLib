using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Coworkee.Client.Blazor.Chat;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LiveUpdatesTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private readonly IChatApi _chat = Substitute.For<IChatApi>();

    public LiveUpdatesTests()
    {
        Services.AddSingleton(_chat);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString())).SetPolicies(Security.PermissionPolicy.For(ChatPermissions.Use));
        Api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns([ChatPermissions.Use]);
        Api.GetUserCardsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([new UserCardDto(Bob, "Bob Builder", null)]);
        Render<MudPopoverProvider>();
    }

    private FakeRealtimeConnection Realtime => Services.GetRequiredService<FakeRealtimeConnection>();

    private static ChatMessageDto Message(Guid from, Guid to, string text) => new(Guid.CreateVersion7(), from, to, text, DateTimeOffset.UtcNow, null);

    [Fact]
    public void A_chat_message_outside_its_conversation_shows_a_snackbar_that_opens_it()
    {
        var snackbar = Render<MudSnackbarProvider>();
        Render<ChatNotifier>();
        snackbar.WaitForAssertion(() => Realtime.Invocations.ShouldContain(("Subscribe", $"user:{Me}")));

        Realtime.Push($"user:{Me}", ChatEvents.Message, Message(Bob, Me, "Lunch?"));

        snackbar.WaitForAssertion(() => snackbar.Markup.ShouldContain("Bob Builder: Lunch?"));
        snackbar.Find(".mud-snackbar-content-action button").Click();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith($"/chat?with={Bob}");
    }

    [Fact]
    public void A_chat_message_of_the_open_conversation_or_from_myself_stays_quiet()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/chat?with={Bob}");
        var snackbar = Render<MudSnackbarProvider>();
        Render<ChatNotifier>();
        snackbar.WaitForAssertion(() => Realtime.Invocations.ShouldContain(("Subscribe", $"user:{Me}")));

        Realtime.Push($"user:{Me}", ChatEvents.Message, Message(Bob, Me, "Lunch?"));
        Realtime.Push($"user:{Me}", ChatEvents.Message, Message(Me, Bob, "Sure"));

        Thread.Sleep(200);
        snackbar.Markup.ShouldNotContain("Lunch?");
        snackbar.Markup.ShouldNotContain("Sure");
    }

    [Fact]
    public async Task The_open_conversation_shows_when_the_other_side_read_it()
    {
        _chat.GetContactsAsync(Arg.Any<CancellationToken>()).Returns([new ChatContactDto(Bob, "Bob Builder", "Hi", DateTimeOffset.UtcNow, 0)]);
        _chat.GetConversationAsync(Bob, null, Arg.Any<CancellationToken>()).Returns([Message(Me, Bob, "Hi")]);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/chat?with={Bob}");
        var page = Render<Pages.Chat>();
        page.WaitForAssertion(() => page.Find("[data-testid='chat-messages']").TextContent.ShouldContain("Hi"));
        page.FindAll("[data-testid='chat-read']").ShouldBeEmpty();

        Realtime.Push($"user:{Me}", ChatEvents.Read, new ChatReadDto(Bob, Me, DateTimeOffset.UtcNow));

        page.WaitForAssertion(() => page.Find("[data-testid='chat-read']"));
        await _chat.Received().GetContactsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_deleted_entity_sends_its_detail_page_back()
    {
        Render<MudSnackbarProvider>();
        var id = Guid.CreateVersion7();
        var changed = 0;
        var watch = Render<EntityWatch>(p => p.Add(c => c.EntityType, "Brand").Add(c => c.EntityId, id).Add(c => c.ReturnHref, "/catalog/brands")
            .Add(c => c.OnChanged, () => changed++));
        var topic = RealtimeTopics.Entity("Brand", id);
        watch.WaitForAssertion(() => Realtime.Invocations.ShouldContain(("Subscribe", topic)));

        Realtime.Push(topic, RealtimeEventTypes.EntityChanged, new EntityChangedPayload("Brand", id.ToString(), EntityChangedPayload.Updated, ["Name"]));
        Realtime.Push(topic, RealtimeEventTypes.EntityChanged, new EntityChangedPayload("Brand", id.ToString(), EntityChangedPayload.Deleted, []));

        watch.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/catalog/brands"));
        changed.ShouldBe(1);
    }

    [Fact]
    public void A_deleted_row_that_belongs_to_the_entity_only_reloads_its_detail_page()
    {
        var id = Guid.CreateVersion7();
        var changed = 0;
        var watch = Render<EntityWatch>(p => p.Add(c => c.EntityType, "User").Add(c => c.EntityId, id).Add(c => c.ReturnHref, "/admin/users")
            .Add(c => c.OnChanged, () => changed++));
        var topic = RealtimeTopics.Entity("User", id);
        watch.WaitForAssertion(() => Realtime.Invocations.ShouldContain(("Subscribe", topic)));

        Realtime.Push(topic, RealtimeEventTypes.EntityChanged, new EntityChangedPayload("IdentityUserRole`1", $"{id},{Guid.CreateVersion7()}", EntityChangedPayload.Deleted, []));

        watch.WaitForAssertion(() => changed.ShouldBe(1));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldNotEndWith("/admin/users");
    }

    [Fact]
    public void An_avatar_shows_whether_its_user_is_online()
    {
        Api.GetOnlineUsersAsync(Arg.Any<CancellationToken>()).Returns([Bob]);
        var avatar = Render<UserAvatar>(p => p.Add(c => c.UserId, Bob));
        avatar.WaitForAssertion(() => avatar.Find("[data-online]").GetAttribute("data-online").ShouldBe("true"));
        avatar.WaitForAssertion(() => Realtime.Invocations.ShouldContain(("Subscribe", PresenceEvents.Topic)));

        Realtime.Push(PresenceEvents.Topic, PresenceEvents.Changed, new PresenceDto(Bob, false));
        avatar.WaitForAssertion(() => avatar.Find("[data-online]").GetAttribute("data-online").ShouldBe("false"));

        Realtime.Push(PresenceEvents.Topic, PresenceEvents.Changed, new PresenceDto(Bob, true));
        avatar.WaitForAssertion(() => avatar.Find("[data-online]").GetAttribute("data-online").ShouldBe("true"));
    }

    [Fact]
    public void The_chat_link_counts_unread_messages_live_and_clears_when_read()
    {
        _chat.GetUnreadCountAsync(Arg.Any<CancellationToken>()).Returns(1, 2, 0);
        var badges = Services.GetRequiredService<Navigation.NavigationBadges>();
        var notifier = Render<ChatNotifier>();
        notifier.WaitForAssertion(() => badges["/chat"].ShouldBe(1));

        Realtime.Push($"user:{Me}", ChatEvents.Message, Message(Bob, Me, "Lunch?"));
        notifier.WaitForAssertion(() => badges["/chat"].ShouldBe(2));

        Realtime.Push($"user:{Me}", ChatEvents.Read, new ChatReadDto(Me, Bob, DateTimeOffset.UtcNow));
        notifier.WaitForAssertion(() => badges["/chat"].ShouldBe(0));
    }

    [Fact]
    public void An_avatar_follows_a_new_picture_of_its_user()
    {
        Api.GetUserCardsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new UserCardDto(Bob, "Bob Builder", null)], [new UserCardDto(Bob, "Bob Builder", "42")]);
        var avatar = Render<UserAvatar>(p => p.Add(c => c.UserId, Bob));
        avatar.WaitForAssertion(() => avatar.Markup.ShouldContain("BB"));

        Realtime.Push(UserEvents.Topic, UserEvents.Changed, new { userId = Bob });

        avatar.WaitForAssertion(() => avatar.Markup.ShouldContain("avatar?v=42"));
        avatar.WaitForAssertion(() => avatar.Find("img").GetAttribute("src").ShouldBe($"api/v1/identity/users/{Bob}/avatar?v=42"));
    }
}
