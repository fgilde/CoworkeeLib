using System.Diagnostics;
using System.Net.Http.Json;
using Coworkee.Application.Events;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime.Tests;

public sealed class EventTargetTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private readonly string _type = $"test.{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.App.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId);

    private RecordingPublisher Recording => (RecordingPublisher)app.App.Services.GetRequiredService<IRealtimePublisher>();

    private List<string> Sent => [.. Recording.Published.Where(p => p.Type == _type).Select(p => p.Topic)];

    [Fact]
    public async Task Every_message_carries_its_id_time_actor_and_correlation()
    {
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await admin.SubscribeAsync("type:Ticket");
        using var activity = new Activity("request").Start();

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            db.Add(new Ticket { Title = "Hello" });
            return await db.SaveChangesAsync(Ct);
        });

        var envelope = await admin.NextAsync(e => e.Topic == "type:Ticket");
        envelope.Id.ShouldNotBeNull().ShouldNotBe(Guid.Empty);
        envelope.At.ShouldNotBeNull().ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        envelope.Actor.ShouldNotBeNull().UserId.ShouldBe(_setup.AdminUserId);
        envelope.CorrelationId.ShouldBe(activity.TraceId.ToHexString());
    }

    [Fact]
    public async Task A_target_reaches_the_online_users_it_names_on_their_user_topics_and_nobody_offline()
    {
        var bob = await UserAsync("bob@acme.test", Ticket.ViewPermission);
        var carl = await UserAsync("carl@acme.test");
        var dana = await UserAsync("dana@acme.test", Ticket.ViewPermission);
        await using var bobListener = await ListenAsync(bob);
        await using var carlListener = await ListenAsync(carl);

        await PublishAsync(_setup.AdminUserId, EventTarget.Permission(Ticket.ViewPermission), new { text = "hi" });

        var envelope = await bobListener.NextAsync(e => e.Type == _type);
        envelope.Topic.ShouldBe(RealtimeTopics.User(bob));
        envelope.Payload.GetProperty("text").GetString().ShouldBe("hi");
        envelope.Actor!.UserId.ShouldBe(_setup.AdminUserId);
        Sent.ShouldBe([RealtimeTopics.User(bob)]);
        await Task.Delay(500, Ct);
        carlListener.Events.ShouldNotContain(e => e.Type == _type);
        Sent.ShouldNotContain(RealtimeTopics.User(dana));
    }

    [Fact]
    public async Task All_goes_once_to_the_topic_of_the_organisation_and_stays_in_it()
    {
        var bob = await UserAsync("bob@acme.test");
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();
        await using var bobListener = await ListenAsync(bob, RealtimeTopics.TenantEvents);
        await using var other = await app.ConnectAsync(otherAdmin, otherTenant);
        await other.SubscribeAsync(RealtimeTopics.TenantEvents);

        await PublishAsync(_setup.AdminUserId, EventTarget.All, new { text = "maintenance" });

        (await bobListener.NextAsync(e => e.Type == _type)).Topic.ShouldBe(RealtimeTopics.TenantEvents);
        Sent.ShouldBe([RealtimeTopics.TenantEvents]);
        await Task.Delay(500, Ct);
        other.Events.ShouldNotContain(e => e.Type == _type);
    }

    [Fact]
    public async Task Except_current_leaves_out_the_one_who_acted_and_current_alone_reaches_him()
    {
        var bob = await UserAsync("bob@acme.test");
        var carl = await UserAsync("carl@acme.test");
        await using var bobListener = await ListenAsync(bob);
        await using var carlListener = await ListenAsync(carl);

        await PublishAsync(bob, EventTarget.All.Except(EventTarget.Current), new { text = "bob saved" });

        await carlListener.NextAsync(e => e.Type == _type);
        Sent.ShouldNotContain(RealtimeTopics.User(bob));

        // without a session (a job, a test) the current user is reached on his own topic
        await PublishAsync(bob, EventTarget.Current, new { text = "done" });
        await bobListener.NextAsync(e => e.Type == _type);
    }

    [Fact]
    public async Task Users_of_another_organisation_are_never_reached_through_ids()
    {
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();
        await using var other = await app.ConnectAsync(otherAdmin, otherTenant);
        await other.SubscribeAsync(RealtimeTopics.User(otherAdmin));

        await PublishAsync(_setup.AdminUserId, EventTarget.Users(otherAdmin), new { text = "guess" });

        await Task.Delay(500, Ct);
        Sent.ShouldBeEmpty();
        other.Events.ShouldNotContain(e => e.Type == _type);
    }

    [Fact]
    public async Task Topics_of_others_stay_refused_and_the_shared_event_topics_are_open_to_every_signed_in_user()
    {
        var bob = await UserAsync("bob@acme.test");
        var carl = await UserAsync("carl@acme.test");
        await using var listener = await app.ConnectAsync(bob, _setup.TenantId);

        await Should.ThrowAsync<HubException>(() => listener.SubscribeAsync(RealtimeTopics.User(carl)));
        await Should.ThrowAsync<HubException>(() => listener.SubscribeAsync(RealtimeTopics.Session("someone-else")));
        await listener.SubscribeAsync(RealtimeTopics.TenantEvents);
        await listener.SubscribeAsync(RealtimeTopics.GlobalEvents);
    }

    [Fact]
    public async Task Inside_a_unit_of_work_events_leave_after_the_commit_and_never_after_a_rollback()
    {
        var bob = await UserAsync("bob@acme.test");
        await using var listener = await ListenAsync(bob);

        await InScopeAsync(async (db, events) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            db.Add(new Ticket { Title = "Rolled back" });
            await events.PublishAsync(EventTarget.Users(bob), _type, new { text = "rolled back" }, Ct);
            await db.SaveChangesAsync(Ct);
            Sent.ShouldBeEmpty();
            await transaction.RollbackAsync(Ct);
        });
        Sent.ShouldBeEmpty();

        await InScopeAsync(async (db, events) =>
        {
            db.Add(new Ticket { Title = "Saved" });
            await events.PublishAsync(EventTarget.Users(bob), _type, new { text = "saved" }, Ct);
            Sent.ShouldBeEmpty();
            await db.SaveChangesAsync(Ct);
        });

        (await listener.NextAsync(e => e.Type == _type)).Payload.GetProperty("text").GetString().ShouldBe("saved");
        Sent.Count.ShouldBe(1);
    }

    private async Task InScopeAsync(Func<RealtimeTestDbContext, RealtimeEvents, Task> action)
    {
        using var current = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<RealtimeTestDbContext>(), scope.ServiceProvider.GetRequiredService<RealtimeEvents>());
    }

    private async Task PublishAsync(Guid actor, EventTarget target, object payload)
    {
        using var current = CurrentUserScope.Begin(new ImpersonatedUser(actor, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RealtimeEvents>().PublishAsync(target, _type, payload, Ct);
    }

    private async Task<Listener> ListenAsync(Guid userId, string? topic = null)
    {
        var listener = await app.ConnectAsync(userId, _setup.TenantId);
        await listener.SubscribeAsync(topic ?? RealtimeTopics.User(userId));
        return listener;
    }

    private async Task<Guid> UserAsync(string email, params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{user.Id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return user.Id;
    }
}
