using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Social;
using Coworkee.Contracts.Social;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SocialComponentsTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private readonly ISocialApi _api = Substitute.For<ISocialApi>();
    private readonly Guid _id = Guid.CreateVersion7();

    public SocialComponentsTests()
    {
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString()));
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    [Fact]
    public async Task Comments_show_replies_under_their_parent_reply_and_reload_on_realtime_events()
    {
        var root = Comment(Bob, "Root");
        var later = Comment(Bob, "Later");
        var reply = Comment(Me, "Mine", root.Id);
        _api.GetCommentsAsync("Products", _id, default).ReturnsForAnyArgs(new CommentThreadDto([root, later, reply], true, false));
        var comments = Render<CoworkeeComments>(p => p.Add(c => c.EntityType, "Products").Add(c => c.EntityId, _id));
        comments.WaitForAssertion(() => comments.FindAll("[data-comment]").Count.ShouldBe(3));

        comments.FindAll("[data-comment]").Select(c => c.GetAttribute("data-comment")).ShouldBe([root.Id.ToString(), reply.Id.ToString(), later.Id.ToString()]);
        comments.FindAll($"[data-comment='{root.Id}'] > div [data-testid='comment-edit']").ShouldBeEmpty();
        comments.Find($"[data-comment='{reply.Id}'] [data-testid='comment-edit']").ShouldNotBeNull();

        await comments.Find($"[data-comment='{root.Id}'] [data-testid='comment-reply']").ClickAsync(new());
        comments.Find($"[data-comment='{root.Id}'] textarea[data-testid='comment-text']").Input("Thanks");
        await comments.Find($"[data-comment='{root.Id}'] [data-testid='comment-send']").ClickAsync(new());
        await _api.Received(1).AddCommentAsync("Products", _id, "Thanks", root.Id, Arg.Any<CancellationToken>());

        _api.GetCommentsAsync("Products", _id, default).ReturnsForAnyArgs(new CommentThreadDto([root], true, false));
        Services.GetRequiredService<FakeRealtimeConnection>().Push(SocialTopics.Comments("Products", _id));
        comments.WaitForAssertion(() => comments.FindAll("[data-comment]").Count.ShouldBe(1));
    }

    [Fact]
    public async Task Tags_are_added_and_removed_through_the_whole_list()
    {
        _api.GetEntityTagsAsync("Products", _id, default).ReturnsForAnyArgs(new EntityTagsDto(["red"], true));
        _api.SaveEntityTagsAsync("Products", _id, default!, default).ReturnsForAnyArgs(call => new EntityTagsDto(call.ArgAt<IReadOnlyList<string>>(2), true));
        var tags = Render<CoworkeeTags>(p => p.Add(t => t.EntityType, "Products").Add(t => t.EntityId, _id));
        tags.WaitForAssertion(() => tags.Find("[data-tag='red']"));

        var input = tags.Find("input[data-testid='tag-input'], [data-testid='tag-input'] input");
        input.Input("blue");
        await input.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        await _api.Received(1).SaveEntityTagsAsync("Products", _id, Arg.Is<IReadOnlyList<string>>(t => t.SequenceEqual(new[] { "red", "blue" })), Arg.Any<CancellationToken>());
        tags.WaitForAssertion(() => tags.Find("[data-tag='blue']"));
    }

    [Fact]
    public void Read_only_tags_and_ratings_offer_no_input()
    {
        _api.GetEntityTagsAsync("Products", _id, default).ReturnsForAnyArgs(new EntityTagsDto([], false));
        _api.GetRatingAsync("Products", _id, default).ReturnsForAnyArgs(new RatingDto(3.5, 4, null, false));

        var tags = Render<CoworkeeTags>(p => p.Add(t => t.EntityType, "Products").Add(t => t.EntityId, _id));
        var rating = Render<CoworkeeRating>(p => p.Add(r => r.EntityType, "Products").Add(r => r.EntityId, _id));

        tags.WaitForAssertion(() => tags.FindAll("[data-testid='tag-input']").ShouldBeEmpty());
        rating.WaitForAssertion(() => rating.Find("[data-testid='rating-summary']").TextContent.ShouldContain("4"));
        rating.FindAll("[data-testid='rating-clear']").ShouldBeEmpty();
    }

    [Fact]
    public void Tag_filter_matches_the_ids_or_nothing() =>
        (CoworkeeTagCloud.ToFilter([]), CoworkeeTagCloud.ToFilter([Me, Bob])).ShouldBe(("false", $"Id in ({Me},{Bob})"));

    private static CommentDto Comment(Guid author, string text, Guid? parentId = null) =>
        new(Guid.CreateVersion7(), parentId, author, text, DateTimeOffset.UtcNow, null);
}
