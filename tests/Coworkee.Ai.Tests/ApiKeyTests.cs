using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.ApiKeys;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;

namespace Coworkee.Ai.Tests;

public sealed class ApiKeyTests(AiApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Keys_are_shown_once_stored_hashed_and_grant_at_most_what_the_user_has()
    {
        var user = app.As(await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat, NotePermissions.Write, IdentityPermissions.Users.View), _setup.TenantId);

        var created = await CreateAsync(user, AiPermissions.Chat, IdentityPermissions.Users.View);

        created.Secret.ShouldStartWith(ApiKeyDefaults.Prefix);
        created.Secret.Length.ShouldBe(ApiKeyDefaults.Prefix.Length + 64);
        created.Key.Prefix.ShouldBe(created.Secret[..10]);
        var stored = await KeyAsync(created.Key.Id);
        stored.SecretHash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Secret))));
        (await user.GetStringAsync("/api/v1/identity/api-keys", Ct)).ShouldNotContain(created.Secret[10..]);

        (await PermissionsAsync(KeyClient(created.Secret))).ShouldBe([AiPermissions.Chat, IdentityPermissions.Users.View], ignoreOrder: true);
        var header = app.App.GetTestClient();
        header.DefaultRequestHeaders.Add(ApiKeyDefaults.Header, created.Secret);
        (await PermissionsAsync(header)).ShouldBe([AiPermissions.Chat, IdentityPermissions.Users.View], ignoreOrder: true);
        (await KeyAsync(created.Key.Id)).LastUsedAt.ShouldNotBeNull();

        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(30), IdentityPermissions.Roles.Manage)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(400), AiPermissions.Chat)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(-1), AiPermissions.Chat)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(30))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // a key cannot make keys, also not with the right scope
        var minting = await CreateAsync(user, ApiKeyPermissions.Create, AiPermissions.Chat);
        (await TryCreateAsync(KeyClient(minting.Secret), DateTimeOffset.UtcNow.AddDays(1), AiPermissions.Chat)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await KeyClient(minting.Secret).PostAsync($"/api/v1/identity/api-keys/{created.Key.Id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/api-keys/scopes", Ct))!.SelectMany(g => g.Permissions).Select(p => p.Name)
            .ShouldBe([AiPermissions.Chat, ApiKeyPermissions.Create, IdentityPermissions.Users.View, NotePermissions.Write], ignoreOrder: true);
    }

    [Fact]
    public async Task A_key_gets_at_most_what_the_session_has_now_within_its_active_role()
    {
        var userId = await app.UserAsync(Admin);
        var user = app.As(userId, _setup.TenantId);
        var writer = await AiApp.RoleAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat, NotePermissions.Write);
        var reader = await AiApp.RoleAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat);
        await AiApp.SetRolesAsync(Admin, userId, writer, reader);
        (await user.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(reader), Ct)).EnsureSuccessStatusCode();

        (await user.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/api-keys/scopes", Ct))!.SelectMany(g => g.Permissions).Select(p => p.Name)
            .ShouldNotContain(NotePermissions.Write);
        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(30), NotePermissions.Write)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(30), AiPermissions.Chat)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_has_a_limited_number_of_active_keys_and_creates_them_slowly()
    {
        var user = app.As(await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat), _setup.TenantId);
        var keys = new List<CreatedApiKeyDto>();
        for (var i = 0; i < 4; i++)
        {
            keys.Add(await CreateAsync(user, AiPermissions.Chat));
        }

        (await TryCreateAsync(user, DateTimeOffset.UtcNow.AddDays(30), AiPermissions.Chat)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await user.PostAsync($"/api/v1/identity/api-keys/{keys[0].Key.Id}/revoke", null, Ct)).EnsureSuccessStatusCode();
        await CreateAsync(user, AiPermissions.Chat);

        var hasty = app.As(await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat), _setup.TenantId);
        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 9; i++)
        {
            answers.Add((await TryCreateAsync(hasty, DateTimeOffset.UtcNow.AddDays(-1), AiPermissions.Chat)).StatusCode);
        }

        answers[..8].ShouldAllBe(s => s == HttpStatusCode.BadRequest);
        answers[8].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Revoked_expired_unknown_and_deactivated_keys_are_refused_at_once()
    {
        var userId = await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat);
        var user = app.As(userId, _setup.TenantId);
        var revoked = await CreateAsync(user, AiPermissions.Chat);
        (await KeyClient(revoked.Secret).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await user.PostAsync($"/api/v1/identity/api-keys/{revoked.Key.Id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await KeyClient(revoked.Secret).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await user.GetFromJsonAsync<ApiKeyDto[]>("/api/v1/identity/api-keys", Ct))!.Single().RevokedAt.ShouldNotBeNull();

        var expired = await CreateAsync(user, AiPermissions.Chat);
        await UpdateAsync(db => db.Set<ApiKey>().IgnoreQueryFilters().Where(k => k.Id == expired.Key.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        (await KeyClient(expired.Secret).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await KeyClient(ApiKeyDefaults.Prefix + new string('0', 64)).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await KeyClient(revoked.Secret[..^1] + "x").GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var active = await CreateAsync(user, AiPermissions.Chat);
        await UpdateAsync(db => db.Set<User>().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false), Ct));
        (await KeyClient(active.Secret).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_key_narrows_with_its_users_permissions_and_ignores_the_active_role()
    {
        var userId = await app.UserAsync(Admin);
        var user = app.As(userId, _setup.TenantId);
        var writer = await AiApp.RoleAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat, NotePermissions.Write);
        var reader = await AiApp.RoleAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat);
        await AiApp.SetRolesAsync(Admin, userId, writer, reader);
        var key = KeyClient((await CreateAsync(user, AiPermissions.Chat, NotePermissions.Write)).Secret);

        // the app works within the chosen role, the key within all roles of its user
        (await user.PutAsJsonAsync("/api/v1/identity/roles/me/active", new SetActiveRoleRequest(reader), Ct)).EnsureSuccessStatusCode();
        (await PermissionsAsync(user)).ShouldNotContain(NotePermissions.Write);
        (await PermissionsAsync(key)).ShouldBe([AiPermissions.Chat, NotePermissions.Write], ignoreOrder: true);

        await AiApp.SetRolesAsync(Admin, userId, reader);
        (await PermissionsAsync(key)).ShouldBe([AiPermissions.Chat]);

        // without the right to have keys, the key is worth nothing
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{reader}", new NameListRequest([AiPermissions.Chat]), Ct)).EnsureSuccessStatusCode();
        (await PermissionsAsync(key)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Admins_see_and_revoke_the_keys_of_their_organisation_users_only_their_own()
    {
        var owner = app.As(await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat), _setup.TenantId);
        var other = app.As(await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat), _setup.TenantId);
        var key = await CreateAsync(owner, AiPermissions.Chat);
        await CreateAsync(other, AiPermissions.Chat);

        (await other.GetFromJsonAsync<ApiKeyDto[]>("/api/v1/identity/api-keys", Ct))!.ShouldNotContain(k => k.Id == key.Key.Id);
        (await other.PostAsync($"/api/v1/identity/api-keys/{key.Key.Id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync("/odata/ApiKeys", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var all = await Admin.GetStringAsync("/odata/ApiKeys", Ct);
        JsonNode.Parse(all)!["value"]!.AsArray().Count.ShouldBe(2);
        all.ShouldNotContain("SecretHash");
        all.ShouldNotContain(key.Secret[10..]);

        // the set only reads: no OData write reaches a key
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Put, HttpMethod.Delete })
        {
            var path = method == HttpMethod.Post ? "/odata/ApiKeys" : $"/odata/ApiKeys({key.Key.Id})";
            var response = await Admin.SendAsync(new HttpRequestMessage(method, path) { Content = JsonContent.Create(new { Name = "x", RevokedAt = (DateTimeOffset?)null, SecretHash = "00" }) }, Ct);
            response.IsSuccessStatusCode.ShouldBeFalse($"{method} {path}");
        }

        (await KeyAsync(key.Key.Id)).ShouldSatisfyAllConditions(k => k.Name.ShouldBe("Claude Desktop"), k => k.RevokedAt.ShouldBeNull());
        (await Admin.GetStringAsync($"/odata/ApiKeys({key.Key.Id})", Ct)).ShouldNotContain("SecretHash");

        // an administrator of another organisation finds nothing
        var stranger = await StrangerWithManageAsync();
        JsonNode.Parse(await stranger.GetStringAsync("/odata/ApiKeys", Ct))!["value"]!.AsArray().ShouldBeEmpty();
        (await stranger.PostAsync($"/api/v1/identity/api-keys/{key.Key.Id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await KeyClient(key.Secret).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.PostAsync($"/api/v1/identity/api-keys/{key.Key.Id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await KeyClient(key.Secret).GetAsync("/api/v1/identity/permissions/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await KeyAsync(key.Key.Id)).RevokedBy.ShouldBe(_setup.AdminUserId);
    }

    [Fact]
    public async Task Mcp_with_a_key_offers_and_runs_only_the_tools_of_its_scopes_as_its_user()
    {
        var userId = await app.UserAsync(Admin, ApiKeyPermissions.Create, AiPermissions.Chat, NotePermissions.Write);
        var user = app.As(userId, _setup.TenantId);
        var reading = await McpAsync((await CreateAsync(user, AiPermissions.Chat)).Secret);
        var writing = await CreateAsync(user, AiPermissions.Chat, NotePermissions.Write);
        var writer = await McpAsync(writing.Secret);
        var noAssistant = await McpAsync((await CreateAsync(user, NotePermissions.Write)).Secret);

        (await reading.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ShouldNotContain("add_note");
        (await reading.CallToolAsync("add_note", new Dictionary<string, object?> { ["title"] = "no" }, cancellationToken: Ct)).IsError.ShouldBe(true);
        (await noAssistant.ListToolsAsync(cancellationToken: Ct)).ShouldBeEmpty();

        (await writer.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ShouldContain("add_note");
        (await writer.CallToolAsync("add_note", new Dictionary<string, object?> { ["title"] = "By key" }, cancellationToken: Ct)).IsError.ShouldNotBe(true);
        (await app.InDbAsync(db => db.Set<Note>().SingleAsync(Ct))).ShouldSatisfyAllConditions(n => n.Title.ShouldBe("By key"), n => n.CreatedBy.ShouldBe(userId));
        var audited = await app.InDbAsync(db => db.Set<AiToolCall>().Where(c => c.Channel == AiChannels.Mcp && c.Succeeded).ToListAsync(Ct));
        audited.ShouldHaveSingleItem().ShouldSatisfyAllConditions(c => c.ApiKeyId.ShouldBe(writing.Key.Id), c => c.UserId.ShouldBe(userId));

        foreach (var client in new[] { reading, writer, noAssistant })
        {
            await client.DisposeAsync();
        }

        // a key is no ambient credential: browser based clients of other origins may use it
        var foreign = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }) };
        foreign.Headers.Add("Origin", "http://localhost:6274");
        foreign.Headers.Accept.ParseAdd("application/json, text/event-stream");
        (await KeyClient(writing.Secret).SendAsync(foreign, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private HttpClient KeyClient(string secret)
    {
        var client = app.App.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private async Task<McpClient> McpAsync(string secret)
    {
        var http = KeyClient(secret);
        return await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http),
            cancellationToken: Ct);
    }

    private static async Task<string[]> PermissionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct))!;

    private static Task<HttpResponseMessage> TryCreateAsync(HttpClient client, DateTimeOffset expires, params string[] scopes) =>
        client.PostAsJsonAsync("/api/v1/identity/api-keys", new CreateApiKeyRequest("Claude Desktop", "on my laptop", expires, scopes), Ct);

    private static async Task<CreatedApiKeyDto> CreateAsync(HttpClient client, params string[] scopes)
    {
        var response = await TryCreateAsync(client, DateTimeOffset.UtcNow.AddDays(30), scopes);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyDto>(Ct))!;
    }

    private Task<ApiKey> KeyAsync(Guid id) => app.InDbAsync(db => db.Set<ApiKey>().IgnoreQueryFilters().AsNoTracking().SingleAsync(k => k.Id == id, Ct));

    private Task UpdateAsync(Func<AiTestDbContext, Task<int>> update) => app.InDbAsync(update);

    private async Task<HttpClient> StrangerWithManageAsync()
    {
        var tenant = new Tenant { Name = "Globex", Identifier = "globex-" + Guid.NewGuid().ToString("N")[..6] };
        var stranger = new User { TenantId = tenant.Id, UserName = $"{Guid.NewGuid():N}@globex.test", Email = "boss@globex.test" };
        await app.InDbAsync(async db =>
        {
            db.Add(tenant);
            db.Add(stranger);
            db.Add(new PermissionGrant { TenantId = tenant.Id, Name = ApiKeyPermissions.Manage, ProviderType = PermissionProviderType.User, ProviderKey = stranger.Id });
            return await db.SaveChangesAsync(Ct);
        });
        return app.As(stranger.Id, tenant.Id);
    }
}
