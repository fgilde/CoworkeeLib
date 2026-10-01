using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Realtime.Tests;

public sealed class RealtimeTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Subscriber_receives_entity_changed_after_commit()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");

        var id = await CreateTicketAsync("Hello");

        var envelope = await admin.NextAsync(e => e.Topic == "type:Ticket");
        envelope.Type.ShouldBe(RealtimeEventTypes.EntityChanged);
        envelope.Payload.GetProperty("entityId").GetString().ShouldBe(id.ToString());
        envelope.Payload.GetProperty("action").GetString().ShouldBe("Created");
    }

    [Fact]
    public async Task Entity_topic_receives_changes_of_that_entity_only()
    {
        var id = await CreateTicketAsync("One");
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync($"entity:Ticket:{id}");

        await CreateTicketAsync("Two");
        await RenameAsync(id, "One more");

        var envelope = await admin.NextAsync(e => e.Topic == $"entity:Ticket:{id}");
        envelope.Payload.GetProperty("action").GetString().ShouldBe("Updated");
        admin.Events.ShouldAllBe(e => e.Topic == $"entity:Ticket:{id}");
    }

    [Fact]
    public async Task Rollback_publishes_nothing()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");

        await Should.ThrowAsync<DbUpdateException>(() => CreateTicketAsync(new string('x', 50)));

        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        admin.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Payload_has_property_names_only()
    {
        var id = await CreateTicketAsync("Public");
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");

        await RenameAsync(id, "secret-title");

        var envelope = await admin.NextAsync(e => e.Payload.GetProperty("action").GetString() == "Updated");
        envelope.Payload.GetProperty("changedProperties").EnumerateArray().Select(p => p.GetString()).ShouldContain("Title");
        envelope.Payload.GetRawText().ShouldNotContain("secret-title");
    }

    [Fact]
    public async Task Subscription_without_permission_is_rejected_but_the_connection_stays()
    {
        var response = await app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId)
            .PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        await using var listener = await app.ConnectAsync(bob.Id, _setup.TenantId);

        await Should.ThrowAsync<HubException>(() => listener.SubscribeAsync("type:Ticket"));

        await listener.SubscribeAsync($"user:{bob.Id}");
    }

    [Theory]
    [InlineData("user:{0}")]
    [InlineData("nope:thing")]
    [InlineData("type:Unknown")]
    public async Task Foreign_user_topic_and_unknown_topics_are_rejected(string pattern)
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);

        await Should.ThrowAsync<HubException>(() => admin.SubscribeAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task Other_tenant_receives_nothing()
    {
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        await using var other = await app.ConnectAsync(otherUser, otherTenant);
        await other.SubscribeAsync("type:Ticket");
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");

        await CreateTicketAsync("Mine");

        await admin.NextAsync(e => e.Topic == "type:Ticket");
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        other.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Role_assignment_and_group_membership_changes_are_published()
    {
        var api = app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        var bob = (await (await api.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = (await (await api.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Editors", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        var group = (await (await api.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Team", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:User");
        await admin.SubscribeAsync("type:UserGroup");

        (await api.PutAsJsonAsync($"/api/v1/identity/users/{bob.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([bob.Id]), Ct)).EnsureSuccessStatusCode();

        await admin.NextAsync(e => e.Topic == "type:User");
        await admin.NextAsync(e => e.Topic == "type:UserGroup");
    }

    [Fact]
    public async Task Connection_is_closed_when_the_token_expires()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId, DateTimeOffset.UtcNow.AddSeconds(2));
        var closed = new TaskCompletionSource();
        admin.Connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    [Fact]
    public async Task Events_inside_a_transaction_wait_for_the_commit()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            db.Add(new Ticket { Title = "Rolled back" });
            await db.SaveChangesAsync(Ct);
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
            admin.Events.ShouldBeEmpty();
            await transaction.RollbackAsync(Ct);
            return 0;
        });
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        admin.Events.ShouldBeEmpty();

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            db.Add(new Ticket { Title = "Committed" });
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
            return 0;
        });
        await admin.NextAsync(e => e.Topic == "type:Ticket");
    }

    [Fact]
    public async Task Derived_types_publish_under_their_root_type()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            db.Add(new UrgentTicket { Title = "Urgent", Priority = 1 });
            return await db.SaveChangesAsync(Ct);
        });

        (await admin.NextAsync(e => e.Topic == "type:Ticket")).Payload.GetProperty("entityType").GetString().ShouldBe("Ticket");
    }

    [Fact]
    public async Task A_resource_grant_allows_the_entity_topic_but_not_the_type_topic()
    {
        var api = app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);
        var bob = (await (await api.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = (await (await api.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Ticket readers", null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await api.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([Ticket.ViewPermission]), Ct)).EnsureSuccessStatusCode();
        var ticket = await CreateTicketAsync("Shared");
        (await api.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Ticket/{ticket}",
            new GrantResourcePermissionRequest(PrincipalType.User, bob.Id, role), Ct)).EnsureSuccessStatusCode();
        await using var listener = await app.ConnectAsync(bob.Id, _setup.TenantId);

        await listener.SubscribeAsync($"entity:Ticket:{ticket}");
        await Should.ThrowAsync<HubException>(() => listener.SubscribeAsync("type:Ticket"));
        await Should.ThrowAsync<HubException>(() => listener.SubscribeAsync($"entity:Ticket:{Guid.CreateVersion7()}"));
    }

    private Task<Guid> CreateTicketAsync(string title) => app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
    {
        var ticket = new Ticket { Title = title };
        db.Add(ticket);
        await db.SaveChangesAsync(Ct);
        return ticket.Id;
    });

    private Task RenameAsync(Guid id, string title) => app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
    {
        (await db.Set<Ticket>().SingleAsync(t => t.Id == id, Ct)).Title = title;
        return await db.SaveChangesAsync(Ct);
    });
}
