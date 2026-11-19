using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Domain;

namespace Coworkee.Settings.Tests;

/// <summary>
/// <c>/api/v1/client-values</c>: free values of a client per user, with global values of the organisation below them. The test app allows 2 keys per user
/// and 1024 bytes per value.
/// </summary>
public sealed class ClientValueTests(SettingsApp app) : IAsyncLifetime
{
    private const string Url = "/api/v1/client-values";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Own_values_lie_over_the_global_ones_and_stay_the_users_own()
    {
        var ann = app.As(await CreateUserAsync("ann@acme.test"), _setup.TenantId);
        var bob = app.As(await CreateUserAsync("bob@acme.test"), _setup.TenantId);

        (await GetOneAsync(ann, "layout.Mine")).ShouldBe(new ClientValueDto("layout.Mine", null, false));
        (await SetAsync(Admin, "layout.Mine", "{\"cols\":[]}", ClientValueScope.Global)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SetAsync(ann, "layout.Mine", "{\"cols\":[\"eta\"]}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SetAsync(ann, "template.Air & more/1", "B1")).EnsureSuccessStatusCode();
        (await SetAsync(ann, "template.Air & more/1", "B2")).EnsureSuccessStatusCode();

        (await GetOneAsync(ann, "layout.Mine")).ShouldBe(new ClientValueDto("layout.Mine", "{\"cols\":[\"eta\"]}", true));
        (await ann.GetFromJsonAsync<List<ClientValueDto>>(Url, Ct)).ShouldBe([
            new ClientValueDto("layout.Mine", "{\"cols\":[\"eta\"]}", true), new ClientValueDto("template.Air & more/1", "B2", true)]);
        (await ann.GetFromJsonAsync<List<string>>(Url + "/keys", Ct)).ShouldBe(["layout.Mine", "template.Air & more/1"]);

        // Bob sees the global value, none of Ann's; his delete takes only his own (none)
        (await bob.GetFromJsonAsync<List<ClientValueDto>>(Url, Ct)).ShouldBe([new ClientValueDto("layout.Mine", "{\"cols\":[]}", false)]);
        (await GetOneAsync(bob, "template.Air & more/1")).Value.ShouldBeNull();
        (await bob.DeleteAsync($"{Url}?key={Uri.EscapeDataString("template.Air & more/1")}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetOneAsync(ann, "template.Air & more/1")).Value.ShouldBe("B2");

        // Ann's delete brings the global value back; the global delete leaves nothing
        (await ann.DeleteAsync($"{Url}?key=layout.Mine", Ct)).EnsureSuccessStatusCode();
        (await GetOneAsync(ann, "layout.Mine")).ShouldBe(new ClientValueDto("layout.Mine", "{\"cols\":[]}", false));
        (await Admin.DeleteAsync($"{Url}?key=layout.Mine&scope=Global", Ct)).EnsureSuccessStatusCode();
        (await GetOneAsync(ann, "layout.Mine")).Value.ShouldBeNull();
    }

    [Fact]
    public async Task Global_values_need_the_permission()
    {
        var bob = app.As(await CreateUserAsync("bob@acme.test"), _setup.TenantId);
        (await SetAsync(Admin, "k", "v", ClientValueScope.Global)).EnsureSuccessStatusCode();

        (await SetAsync(bob, "k", "mine", ClientValueScope.Global)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.DeleteAsync($"{Url}?key=k&scope=Global", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetOneAsync(bob, "k")).ShouldBe(new ClientValueDto("k", "v", false));
    }

    [Fact]
    public async Task Another_organisation_sees_neither_the_global_nor_the_own_values()
    {
        var ann = await CreateUserAsync("ann@acme.test");
        (await SetAsync(Admin, "shared", "acme", ClientValueScope.Global)).EnsureSuccessStatusCode();
        (await SetAsync(app.As(ann, _setup.TenantId), "own", "ann")).EnsureSuccessStatusCode();
        var (otherAdmin, other) = await app.CreateTenantAdminAsync();

        (await app.As(otherAdmin, other).GetFromJsonAsync<List<ClientValueDto>>(Url, Ct)).ShouldBeEmpty();
        // the same user id in another organisation: nothing of Ann's
        (await app.As(ann, other).GetFromJsonAsync<List<ClientValueDto>>(Url, Ct)).ShouldBeEmpty();
        // and its own global value stays its own
        (await SetAsync(app.As(otherAdmin, other), "shared", "other", ClientValueScope.Global)).EnsureSuccessStatusCode();
        (await GetOneAsync(Admin, "shared")).Value.ShouldBe("acme");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    public async Task Bad_keys_are_refused(string key)
    {
        (await SetAsync(Admin, key, "v")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetAsync($"{Url}/value?key={Uri.EscapeDataString(key)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.DeleteAsync($"{Url}?key={Uri.EscapeDataString(key)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Long_keys_large_values_and_too_many_keys_are_refused()
    {
        (await SetAsync(Admin, new string('k', ClientValue.KeyLength + 1), "v")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SetAsync(Admin, "big", new string('x', 1024))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // 1024 bytes in UTF-8 are fewer characters
        (await SetAsync(Admin, "big", new string('ä', 513))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await SetAsync(Admin, "a", "1")).EnsureSuccessStatusCode();
        (await SetAsync(Admin, "b", "1")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // an existing key still changes, and global values do not count
        (await SetAsync(Admin, "a", "2")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SetAsync(Admin, "b", "1", ClientValueScope.Global)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public void Own_values_reach_only_their_user_and_global_values_the_organisation()
    {
        var topics = new ClientValueTopics();
        var user = Guid.NewGuid();

        topics.TopicsFor(new ClientValue { UserId = user, Key = "k" }).ShouldBe([RealtimeTopics.User(user)]);
        topics.TopicsFor(new ClientValue { Key = "k" }).ShouldBe([SettingTopics.ClientValues]);
        topics.TopicsFor(new SettingValue { Name = "x" }).ShouldBeEmpty();
    }

    private Task<HttpResponseMessage> SetAsync(HttpClient client, string key, string value, ClientValueScope scope = ClientValueScope.User) =>
        client.PutAsJsonAsync(Url, new SetClientValueRequest(key, value, scope), Coworkee.Contracts.CoworkeeJson.Web, Ct);

    private static async Task<ClientValueDto> GetOneAsync(HttpClient client, string key) =>
        (await client.GetFromJsonAsync<ClientValueDto>($"{Url}/value?key={Uri.EscapeDataString(key)}", Ct))!;

    private async Task<Guid> CreateUserAsync(string email)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }
}
