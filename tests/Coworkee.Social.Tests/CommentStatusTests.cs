using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Social;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Social.Tests;

public sealed class CommentStatusTests(SocialApp app) : IAsyncLifetime
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
    public async Task An_open_comment_moves_the_note_to_request_and_closing_it_moves_it_back_after_the_commit()
    {
        var request = await ReadAsync<CommentDto>(await Admin.PostAsJsonAsync(Comments,
            new CommentRequest("Price for 40ft?", Status: CommentStatus.Open, Properties: new Dictionary<string, string> { ["ReferenceNumber"] = "Q-1" }), Ct));

        (request.Status, request.Properties!["ReferenceNumber"]).ShouldBe((CommentStatus.Open, "Q-1"));
        await StateBecomesAsync("Request");

        var closed = await ReadAsync<CommentDto>(await Admin.PutAsJsonAsync($"/api/v1/comments/{request.Id}/status", new CommentStatusRequest(CommentStatus.Closed), Ct));
        closed.Status.ShouldBe(CommentStatus.Closed);
        await StateBecomesAsync("Open");

        var thread = (await Admin.GetFromJsonAsync<CommentThreadDto>(Comments, Ct))!;
        (thread.CanSetStatus, thread.Comments.Single().Status, thread.Comments.Single().Properties!["ReferenceNumber"]).ShouldBe((true, CommentStatus.Closed, "Q-1"));
    }

    [Fact]
    public async Task Changing_a_status_needs_its_permission_authors_only_give_one_when_they_write()
    {
        var bob = await CreateUserAsync("bob@acme.test", SocialPermissions.Comments.Create);
        var asBob = app.As(bob.Id, _setup.TenantId);
        var mine = await ReadAsync<CommentDto>(await asBob.PostAsJsonAsync(Comments, new CommentRequest("Question", Status: CommentStatus.Open), Ct));
        var admins = await ReadAsync<CommentDto>(await Admin.PostAsJsonAsync(Comments, new CommentRequest("Other"), Ct));

        (await asBob.PutAsJsonAsync($"/api/v1/comments/{admins.Id}/status", new CommentStatusRequest(CommentStatus.Open), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asBob.PutAsJsonAsync($"/api/v1/comments/{mine.Id}/status", new CommentStatusRequest(CommentStatus.Closed), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await asBob.PutAsJsonAsync($"/api/v1/comments/{mine.Id}", new CommentRequest("Question", Status: CommentStatus.Closed), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync<CommentDto>(await asBob.PutAsJsonAsync($"/api/v1/comments/{mine.Id}", new CommentRequest("Question?", Status: CommentStatus.Open), Ct))).Status
            .ShouldBe(CommentStatus.Open);

        // closed by someone with SetStatus: the author cannot reopen it
        (await Admin.PutAsJsonAsync($"/api/v1/comments/{mine.Id}/status", new CommentStatusRequest(CommentStatus.Closed), Ct)).EnsureSuccessStatusCode();
        (await asBob.PutAsJsonAsync($"/api/v1/comments/{mine.Id}", new CommentRequest("Still open", Status: CommentStatus.Open), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync<CommentDto>(await asBob.PutAsJsonAsync($"/api/v1/comments/{mine.Id}", new CommentRequest("Thanks"), Ct))).Status.ShouldBe(CommentStatus.Closed);
        (await asBob.GetFromJsonAsync<CommentThreadDto>(Comments, Ct))!.CanSetStatus.ShouldBeFalse();
        (await app.As(_setup.AdminUserId, Guid.CreateVersion7()).PutAsJsonAsync($"/api/v1/comments/{mine.Id}/status", new CommentStatusRequest(CommentStatus.Open), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_app_check_refuses_comments_on_booked_notes()
    {
        var booked = await app.AddNoteAsync(_setup.AdminUserId, _setup.TenantId, SocialApp.Booked);

        var refused = await Admin.PostAsJsonAsync($"/api/v1/comments/Notes/{booked}", new CommentRequest("Too late"), Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.GetFromJsonAsync<CommentThreadDto>($"/api/v1/comments/Notes/{booked}", Ct))!.Comments.ShouldBeEmpty();
        (await Admin.PostAsJsonAsync(Comments, new CommentRequest("Fine", Properties: new Dictionary<string, string> { [new string('k', 51)] = "v" }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task StateBecomesAsync(string state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await StateAsync() != state)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the note never became {state}");
            await Task.Delay(100, Ct);
        }
    }

    private async Task<string> StateAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SocialTestDbContext>().Set<Note>().Where(n => n.Id == _note).Select(n => n.State).SingleAsync(Ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        var role = await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest($"Social {email}", null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user;
    }
}
