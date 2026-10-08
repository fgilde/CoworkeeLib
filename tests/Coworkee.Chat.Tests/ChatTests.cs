using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Chat;
using Coworkee.Contracts.Identity;

namespace Coworkee.Chat.Tests;

public sealed class ChatTests(ChatApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await CreateUserAsync("bob@acme.test", ChatPermissions.Use);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    [Fact]
    public async Task Messages_reach_the_other_person_and_count_as_unread_until_read()
    {
        (await Admin.PostAsJsonAsync($"/api/v1/chat/conversations/{_bob.Id}", new SendChatMessageRequest(" Hi Bob "), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/chat/conversations/{_bob.Id}", new SendChatMessageRequest("Lunch?"), Ct)).EnsureSuccessStatusCode();

        var contacts = await Bob.GetFromJsonAsync<List<ChatContactDto>>("/api/v1/chat/contacts", Ct);
        var ada = contacts!.Single(c => c.UserId == _setup.AdminUserId);
        (ada.Name, ada.LastMessage, ada.Unread).ShouldBe(("Ada Admin", "Lunch?", 2));
        var conversation = await Bob.GetFromJsonAsync<List<ChatMessageDto>>($"/api/v1/chat/conversations/{_setup.AdminUserId}", Ct);
        conversation!.Select(m => m.Text).ShouldBe(["Hi Bob", "Lunch?"]);

        (await Bob.PostAsync($"/api/v1/chat/conversations/{_setup.AdminUserId}/read", null, Ct)).EnsureSuccessStatusCode();

        (await Bob.GetFromJsonAsync<List<ChatContactDto>>("/api/v1/chat/contacts", Ct))!.Single(c => c.UserId == _setup.AdminUserId).Unread.ShouldBe(0);
        (await Admin.GetFromJsonAsync<List<ChatContactDto>>("/api/v1/chat/contacts", Ct))!.Single(c => c.UserId == _bob.Id).Unread.ShouldBe(0);
    }

    [Fact]
    public async Task Only_people_of_the_organisation_with_the_permission_can_chat()
    {
        (await Admin.PostAsJsonAsync($"/api/v1/chat/conversations/{Guid.NewGuid()}", new SendChatMessageRequest("hello?"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PostAsJsonAsync($"/api/v1/chat/conversations/{_bob.Id}", new SendChatMessageRequest(""), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var carol = await CreateUserAsync("carol@acme.test");

        (await app.As(carol.Id, _setup.TenantId).GetAsync("/api/v1/chat/contacts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(_bob.Id, Guid.NewGuid()).GetAsync("/api/v1/chat/contacts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest($"Chat {email}", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }
}
