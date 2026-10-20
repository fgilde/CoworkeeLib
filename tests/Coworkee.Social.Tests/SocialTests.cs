using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Social;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coworkee.Social.Tests;

public sealed class SocialTests(SocialApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _note;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _note = await app.AddNoteAsync(_setup.AdminUserId, _setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private string Comments => $"/api/v1/comments/Notes/{_note}";

    [Fact]
    public async Task Replies_thread_notify_the_owner_and_only_authors_or_moderators_change_comments()
    {
        var bob = await CreateUserAsync("bob@acme.test", SocialPermissions.Comments.Create);
        var asBob = app.As(bob.Id, _setup.TenantId);
        var first = await ReadAsync<CommentDto>(await Admin.PostAsJsonAsync(Comments, new CommentRequest("Looks good"), Ct));

        var reply = await ReadAsync<CommentDto>(await asBob.PostAsJsonAsync(Comments, new CommentRequest(" Agreed ", first.Id), Ct));

        (reply.ParentId, reply.Text, reply.AuthorId).ShouldBe((first.Id, "Agreed", bob.Id));
        var notification = (await app.NotificationsAsync()).ShouldHaveSingleItem();
        (notification.UserId, notification.Link, notification.Title, notification.Body).ShouldBe((_setup.AdminUserId, $"/notes/{_note}", "New comment", "{0}"));
        notification.Arguments.ShouldBe(["Agreed"]);
        var thread = (await asBob.GetFromJsonAsync<CommentThreadDto>(Comments, Ct))!;
        (thread.Comments.Count, thread.CanComment, thread.CanModerate).ShouldBe((2, true, false));

        (await asBob.PutAsJsonAsync($"/api/v1/comments/{first.Id}", new CommentRequest("Hijacked"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asBob.DeleteAsync($"/api/v1/comments/{first.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync<CommentDto>(await asBob.PutAsJsonAsync($"/api/v1/comments/{reply.Id}", new CommentRequest("Agreed!"), Ct))).EditedAt.ShouldNotBeNull();
        (await Admin.PostAsJsonAsync(Comments, new CommentRequest(""), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.DeleteAsync($"/api/v1/comments/{first.Id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<CommentThreadDto>(Comments, Ct))!.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task New_comments_reach_subscribers_live()
    {
        var (connection, events) = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await using var _ = connection;
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Comments("Notes", _note), Ct);

        (await Admin.PostAsJsonAsync(Comments, new CommentRequest("Live"), Ct)).EnsureSuccessStatusCode();

        for (var i = 0; i < 50 && events.IsEmpty; i++)
        {
            await Task.Delay(100, Ct);
        }

        events.ShouldContain(e => e.Topic == SocialTopics.Comments("Notes", _note));
        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Comments("Unknown", _note), Ct));
    }

    [Fact]
    public async Task Tags_form_a_set_per_type_and_find_their_entities()
    {
        var other = await app.AddNoteAsync(_setup.AdminUserId, _setup.TenantId);
        await SaveTagsAsync(_note, "Red", "blue", "red ");
        var tags = await SaveTagsAsync(other, "RED");

        tags.Tags.ShouldBe(["Red"]);
        (await Admin.GetFromJsonAsync<List<TagDto>>("/api/v1/tags/Notes", Ct)).ShouldBe([new TagDto("Red", 2), new TagDto("blue", 1)]);
        (await Admin.GetFromJsonAsync<List<Guid>>("/api/v1/tags/Notes/entities?tag=red", Ct)).ShouldBe([_note, other], ignoreOrder: true);

        (await Admin.DeleteAsync("/api/v1/tags/Notes?tag=red", Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<EntityTagsDto>($"/api/v1/tags/Notes/{_note}", Ct))!.Tags.ShouldBe(["blue"]);
    }

    [Fact]
    public async Task Ratings_average_one_vote_per_person()
    {
        var bob = await CreateUserAsync("rater@acme.test", SocialPermissions.Ratings.Create);
        var url = $"/api/v1/ratings/Notes/{_note}";
        await ReadAsync<RatingDto>(await Admin.PutAsJsonAsync(url, new RateRequest(5), Ct));
        await ReadAsync<RatingDto>(await Admin.PutAsJsonAsync(url, new RateRequest(4), Ct));

        var rating = await ReadAsync<RatingDto>(await app.As(bob.Id, _setup.TenantId).PutAsJsonAsync(url, new RateRequest(1), Ct));

        rating.ShouldBe(new RatingDto(2.5, 2, 1, true));
        (await Admin.PutAsJsonAsync(url, new RateRequest(6), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await app.As(bob.Id, _setup.TenantId).DeleteAsync($"{url}?userId={_setup.AdminUserId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync<RatingDto>(await Admin.DeleteAsync($"{url}?userId={bob.Id}", Ct))).ShouldBe(new RatingDto(4, 1, 4, true));
    }

    [Fact]
    public async Task Personal_data_lists_own_comments_and_ratings_and_erasing_the_user_removes_them()
    {
        var bob = await CreateUserAsync("leaver@acme.test", SocialPermissions.Comments.Create, SocialPermissions.Ratings.Create, SocialPermissions.Reactions.Create);
        var asBob = app.As(bob.Id, _setup.TenantId);
        await ReadAsync<CommentDto>(await asBob.PostAsJsonAsync(Comments, new CommentRequest("My two cents"), Ct));
        await ReadAsync<RatingDto>(await asBob.PutAsJsonAsync($"/api/v1/ratings/Notes/{_note}", new RateRequest(3), Ct));
        await ReadAsync<ReactionsDto>(await asBob.PostAsJsonAsync($"/api/v1/reactions/Notes/{_note}", new ReactionRequest("👍"), Ct));

        var export = await asBob.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/identity/me/personal-data", Ct);
        export.GetProperty("social").GetProperty("comments")[0].GetProperty("text").GetString().ShouldBe("My two cents");
        export.GetProperty("social").GetProperty("ratings")[0].GetProperty("stars").GetInt32().ShouldBe(3);
        export.GetProperty("social").GetProperty("reactions")[0].GetProperty("emoji").GetString().ShouldBe("👍");

        (await Admin.DeleteAsync($"/api/v1/identity/users/{bob.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetFromJsonAsync<CommentThreadDto>(Comments, Ct))!.Comments.ShouldBeEmpty();
        (await Admin.GetFromJsonAsync<RatingDto>($"/api/v1/ratings/Notes/{_note}", Ct))!.Count.ShouldBe(0);
        (await Admin.GetFromJsonAsync<ReactionsDto>($"/api/v1/reactions/Notes/{_note}", Ct))!.Reactions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unregistered_types_foreign_or_hidden_entities_and_missing_permissions_are_rejected()
    {
        var foreign = await app.AddNoteAsync(null, Guid.NewGuid());
        var secret = await app.AddNoteAsync(_setup.AdminUserId, _setup.TenantId, SocialApp.Secret);
        var viewer = await CreateUserAsync("viewer@acme.test");

        (await Admin.GetAsync($"/api/v1/comments/Unknown/{_note}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/comments/Notes/{foreign}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/ratings/Notes/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync($"/api/v1/tags/Notes/{secret}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(viewer.Id, _setup.TenantId).GetAsync(Comments, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rows_hidden_by_the_row_filter_of_the_set_are_not_found_and_their_topics_refused()
    {
        var hidden = await app.AddNoteAsync(_setup.AdminUserId, _setup.TenantId, SocialApp.Private);
        var bob = await CreateUserAsync("outsider@acme.test",
            SocialPermissions.Comments.Create, SocialPermissions.Tags.Create, SocialPermissions.Ratings.Create, SocialPermissions.Reactions.Create);
        var asBob = app.As(bob.Id, _setup.TenantId);
        await ReadAsync<CommentDto>(await Admin.PostAsJsonAsync($"/api/v1/comments/Notes/{hidden}", new CommentRequest("Only for me"), Ct));
        await SaveTagsAsync(hidden, "private-tag");
        await SaveTagsAsync(_note, "private-tag");

        foreach (var url in new[] { "comments", "ratings", "reactions", "tags" }.Select(f => $"/api/v1/{f}/Notes/{hidden}"))
        {
            (await asBob.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Admin.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await asBob.PostAsJsonAsync($"/api/v1/comments/Notes/{hidden}", new CommentRequest("Hi"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asBob.PutAsJsonAsync($"/api/v1/ratings/Notes/{hidden}", new RateRequest(1), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asBob.PostAsJsonAsync($"/api/v1/reactions/Notes/{hidden}", new ReactionRequest("👍"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asBob.PutAsJsonAsync($"/api/v1/tags/Notes/{hidden}", new SaveTagsRequest(["mine"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asBob.GetFromJsonAsync<List<Guid>>("/api/v1/tags/Notes/entities?tag=private-tag", Ct))!.ShouldBe([_note]);
        (await asBob.GetFromJsonAsync<List<TagDto>>("/api/v1/tags/Notes", Ct))!.ShouldContain(t => t.Name == "private-tag" && t.Count == 1);
        (await Admin.GetFromJsonAsync<List<Guid>>("/api/v1/tags/Notes/entities?tag=private-tag", Ct))!.Count.ShouldBe(2);

        var (connection, _) = await app.ConnectAsync(bob.Id, _setup.TenantId);
        await using var __ = connection;
        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Comments("Notes", hidden), Ct));
        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Reactions("Notes", hidden), Ct));
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Comments("Notes", _note), Ct);
    }

    [Fact]
    public async Task Reactions_toggle_per_user_and_emoji_list_who_reacted_and_reach_subscribers_live()
    {
        var bob = await CreateUserAsync("fan@acme.test", SocialPermissions.Reactions.Create);
        var viewer = await CreateUserAsync("looker@acme.test", SocialPermissions.Reactions.View);
        var url = $"/api/v1/reactions/Notes/{_note}";
        var (connection, events) = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await using var _ = connection;
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Reactions("Notes", _note), Ct);

        await ReadAsync<ReactionsDto>(await Admin.PostAsJsonAsync(url, new ReactionRequest("👍"), Ct));
        await ReadAsync<ReactionsDto>(await app.As(bob.Id, _setup.TenantId).PostAsJsonAsync(url, new ReactionRequest("👍"), Ct));
        var reactions = await ReadAsync<ReactionsDto>(await app.As(bob.Id, _setup.TenantId).PostAsJsonAsync(url, new ReactionRequest("🎉"), Ct));

        reactions.CanReact.ShouldBeTrue();
        reactions.Reactions.Select(r => (r.Emoji, r.UserIds.Count)).ShouldBe([("👍", 2), ("🎉", 1)]);
        reactions.Reactions[0].UserIds.ShouldBe([_setup.AdminUserId, bob.Id]);

        var taken = await ReadAsync<ReactionsDto>(await Admin.PostAsJsonAsync(url, new ReactionRequest("👍"), Ct));
        taken.Reactions[0].UserIds.ShouldBe([bob.Id]);
        (await app.As(viewer.Id, _setup.TenantId).GetFromJsonAsync<ReactionsDto>(url, Ct))!.CanReact.ShouldBeFalse();
        (await app.As(viewer.Id, _setup.TenantId).PostAsJsonAsync(url, new ReactionRequest("👍"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsJsonAsync(url, new ReactionRequest("ok"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        for (var i = 0; i < 50 && events.IsEmpty; i++)
        {
            await Task.Delay(100, Ct);
        }

        events.ShouldContain(e => e.Topic == SocialTopics.Reactions("Notes", _note));
    }

    [Fact]
    public async Task Comment_reactions_come_with_the_thread_follow_the_comment_rights_and_go_with_the_comment()
    {
        var reader = await CreateUserAsync("reader@acme.test", SocialPermissions.Reactions.Create);
        var comment = await ReadAsync<CommentDto>(await Admin.PostAsJsonAsync(Comments, new CommentRequest("Nice"), Ct));
        var url = $"/api/v1/reactions/{SocialReactions.Comment}/{comment.Id}";
        var (connection, events) = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await using var _ = connection;
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, SocialTopics.Comments("Notes", _note), Ct);

        await ReadAsync<ReactionsDto>(await Admin.PostAsJsonAsync(url, new ReactionRequest("❤️"), Ct));

        var thread = (await Admin.GetFromJsonAsync<CommentThreadDto>(Comments, Ct))!;
        (thread.ShowReactions, thread.CanReact).ShouldBe((true, true));
        thread.Comments.Single().Reactions.ShouldHaveSingleItem().ShouldBe(new ReactionDto("❤️", [_setup.AdminUserId]), new ReactionDtoComparer());
        (await app.As(reader.Id, _setup.TenantId).PostAsJsonAsync(url, new ReactionRequest("❤️"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetAsync($"/api/v1/reactions/{SocialReactions.Comment}/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        for (var i = 0; i < 50 && events.IsEmpty; i++)
        {
            await Task.Delay(100, Ct);
        }

        events.ShouldContain(e => e.Topic == SocialTopics.Comments("Notes", _note));

        (await Admin.DeleteAsync($"/api/v1/comments/{comment.Id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.ReactionCountAsync()).ShouldBe(0);
    }

    private sealed class ReactionDtoComparer : IEqualityComparer<ReactionDto>
    {
        public bool Equals(ReactionDto? x, ReactionDto? y) => x?.Emoji == y?.Emoji && x!.UserIds.SequenceEqual(y!.UserIds);

        public int GetHashCode(ReactionDto obj) => obj.Emoji.GetHashCode();
    }

    private async Task<EntityTagsDto> SaveTagsAsync(Guid note, params string[] tags) =>
        await ReadAsync<EntityTagsDto>(await Admin.PutAsJsonAsync($"/api/v1/tags/Notes/{note}", new SaveTagsRequest(tags), Ct));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        if (permissions.Length > 0)
        {
            var role = await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest($"Social {email}", null), Ct));
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }
}
