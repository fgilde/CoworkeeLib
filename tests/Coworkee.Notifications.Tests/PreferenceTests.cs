using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Notifications.Tests;

public sealed class PreferenceTests(NotificationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;
    private UserDto _carl = null!;
    private Guid _shipment;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await CreateUserAsync("bob@acme.test", Shipment.ViewShipments);
        _carl = await CreateUserAsync("carl@acme.test", Shipment.ViewShipments);
        _shipment = await app.AddShipmentAsync(_setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    private string Follow(Guid id) => $"/api/v1/notifications/follows/Shipments/{id}";

    [Fact]
    public async Task Preferences_list_the_registered_types_and_decide_who_gets_what_by_which_channel()
    {
        var preferences = (await Bob.GetFromJsonAsync<NotificationPreferenceDto[]>("/api/v1/notifications/preferences", Ct))!;
        preferences.Select(p => (p.Type, p.Category, p.Scope, p.InApp, p.Email)).ShouldBe(
            [(TestNotificationModule.Eta, "Shipment", NotificationScope.Followed, true, true), (TestNotificationModule.Status, "Shipment", NotificationScope.Off, true, false)]);
        (await Bob.PutAsJsonAsync("/api/v1/notifications/preferences/unknown", new SaveNotificationPreferenceRequest(NotificationScope.All, true, false), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Status is off by default: addressed directly Bob gets nothing; switched on, by mail only
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Status) with { Recipients = [_bob.Id] });
        (await InboxAsync(Bob)).ShouldBeEmpty();
        (await Bob.PutAsJsonAsync($"/api/v1/notifications/preferences/{TestNotificationModule.Status}", new SaveNotificationPreferenceRequest(NotificationScope.All, false, true), Ct))
            .EnsureSuccessStatusCode();
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Status) with { Audience = [_bob.Id, _carl.Id, _setup.AdminUserId] });

        (await InboxAsync(Bob)).ShouldBeEmpty();
        var mail = (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem();
        (mail.TemplateName, mail.Subject).ShouldBe((NotificationMails.Template, "Departed"));
        mail.HtmlBody.ShouldContain($"https://app.test/shipments/{_shipment}");
        (await MailsToAsync("carl@acme.test")).ShouldBeEmpty();
        (await MailsToAsync("admin@acme.test")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Followers_hear_about_their_entities_the_audience_by_its_scope_and_the_actor_not_at_all()
    {
        (await Bob.PutAsJsonAsync(Follow(_shipment), new FollowRequest("S-1"), Ct)).EnsureSuccessStatusCode();
        (await Bob.GetFromJsonAsync<FollowStateDto>(Follow(_shipment), Ct))!.ShouldBe(new FollowStateDto(true, true));
        (await Bob.GetFromJsonAsync<FollowDto[]>("/api/v1/notifications/follows", Ct))!.Single().ShouldSatisfyAllConditions(
            f => f.Title.ShouldBe("S-1"), f => f.Link.ShouldBe($"/shipments/{_shipment}"));
        (await Bob.PutAsJsonAsync($"/api/v1/notifications/preferences/{TestNotificationModule.Eta}", new SaveNotificationPreferenceRequest(NotificationScope.Followed, true, false), Ct))
            .EnsureSuccessStatusCode();
        (await app.As(_carl.Id, _setup.TenantId).PutAsJsonAsync($"/api/v1/notifications/preferences/{TestNotificationModule.Eta}",
            new SaveNotificationPreferenceRequest(NotificationScope.All, true, false), Ct)).EnsureSuccessStatusCode();

        await app.PublishAsync(_bob.Id, _setup.TenantId, Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment, Audience = [_bob.Id, _carl.Id] });
        (await InboxAsync(Bob)).ShouldBeEmpty();

        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment, Audience = [_bob.Id, _carl.Id, _setup.AdminUserId] });
        (await InboxAsync(Bob)).Single().Category.ShouldBe("Shipment");
        (await InboxAsync(app.As(_carl.Id, _setup.TenantId))).Count.ShouldBe(2);
        (await InboxAsync(app.As(_setup.AdminUserId, _setup.TenantId))).ShouldBeEmpty();

        (await Bob.DeleteAsync(Follow(_shipment), Ct)).EnsureSuccessStatusCode();
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment });
        (await InboxAsync(Bob)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Following_needs_the_entity_set_and_the_row_and_stays_in_its_organisation()
    {
        var hidden = await app.AddShipmentAsync(_setup.TenantId, Shipment.Hidden);
        var foreign = await app.AddShipmentAsync(Guid.CreateVersion7());
        var nobody = await CreateUserAsync("nobody@acme.test");

        (await app.As(nobody.Id, _setup.TenantId).PutAsJsonAsync(Follow(_shipment), new FollowRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Bob.PutAsJsonAsync(Follow(hidden), new FollowRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Bob.PutAsJsonAsync(Follow(foreign), new FollowRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Bob.PutAsJsonAsync($"/api/v1/notifications/follows/Unknown/{_shipment}", new FollowRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Bob.PutAsJsonAsync(Follow(_shipment), new FollowRequest(), Ct)).EnsureSuccessStatusCode();
        (await app.InDbAsync(_setup.TenantId, db => db.Set<NotificationFollow>().CountAsync(Ct))).ShouldBe(1);
        (await app.InDbAsync(Guid.CreateVersion7(), db => db.Set<NotificationFollow>().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Followers_who_lost_the_permission_or_the_row_hear_nothing_and_guessed_ids_cannot_be_followed()
    {
        var dora = await CreateUserAsync("dora@acme.test", Shipment.ViewShipments);
        foreach (var user in new[] { _bob, _carl, dora })
        {
            (await app.As(user.Id, _setup.TenantId).PutAsJsonAsync(Follow(_shipment), new FollowRequest(), Ct)).EnsureSuccessStatusCode();
        }

        (await app.As(_setup.AdminUserId, _setup.TenantId).PutAsJsonAsync($"/api/v1/identity/users/{_bob.Id}/roles", new IdListRequest([]), Ct)).EnsureSuccessStatusCode();
        HiddenShipments.OutOfScope[_carl.Id] = true;
        try
        {
            await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment });
        }
        finally
        {
            HiddenShipments.OutOfScope.TryRemove(_carl.Id, out _);
        }

        (await InboxAsync(Bob)).ShouldBeEmpty();
        (await InboxAsync(app.As(_carl.Id, _setup.TenantId))).ShouldBeEmpty();
        (await InboxAsync(app.As(dora.Id, _setup.TenantId))).ShouldHaveSingleItem();
        (await app.As(dora.Id, _setup.TenantId).PutAsJsonAsync(Follow(Guid.CreateVersion7()), new FollowRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Done_takes_a_notification_out_of_the_open_inbox_and_counts_follow_the_categories()
    {
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Eta) with { Recipients = [_bob.Id] });
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        var eta = (await InboxAsync(Bob)).Single(n => n.Type == TestNotificationModule.Eta);

        (await Bob.PostAsync($"/api/v1/notifications/{eta.Id}/done", null, Ct)).EnsureSuccessStatusCode();

        var done = (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?done=true", Ct))!.Items.Single();
        (done.Id, done.DoneAt is not null, done.ReadAt is not null).ShouldBe((eta.Id, true, true));
        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?done=false", Ct))!.Items.Single().Title.ShouldBe("Document shared");
        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?search=Grace", Ct))!.Items.Single().Title.ShouldBe("Document shared");
        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?category=Shipment", Ct))!.Items.Single().Id.ShouldBe(eta.Id);
        (await Bob.GetFromJsonAsync<NotificationCountsDto>("/api/v1/notifications/counts", Ct))!.ShouldSatisfyAllConditions(
            c => c.Open.ShouldBe(1), c => c.Unread.ShouldBe(1), c => c.Categories.ShouldBeEmpty());

        (await Bob.PostAsync($"/api/v1/notifications/{eta.Id}/undone", null, Ct)).EnsureSuccessStatusCode();
        (await Bob.GetFromJsonAsync<NotificationCountsDto>("/api/v1/notifications/counts", Ct))!.Categories["Shipment"].ShouldBe(1);
        (await app.As(_carl.Id, _setup.TenantId).PostAsync($"/api/v1/notifications/{eta.Id}/done", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delayed_mails_wait_and_a_newer_change_of_the_same_entity_replaces_the_waiting_one()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (await Bob.PutAsJsonAsync(Follow(_shipment), new FollowRequest(), Ct)).EnsureSuccessStatusCode();
        var eta = Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment };

        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, eta with { Title = "ETA 1 May" }, clock);
        clock.Advance(TimeSpan.FromSeconds(50));
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, eta with { Title = "ETA 2 May" }, clock);
        clock.Advance(TimeSpan.FromSeconds(50));
        await app.RunMailJobAsync(clock);
        (await MailsToAsync("bob@acme.test")).ShouldBeEmpty();

        clock.Advance(TimeSpan.FromSeconds(10));
        await app.RunMailJobAsync(clock);
        (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem().Subject.ShouldBe("ETA 2 May");
        (await InboxAsync(Bob)).Count.ShouldBe(2);

        await app.RunMailJobAsync(clock);
        (await MailsToAsync("bob@acme.test")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_digest_leaves_out_what_went_out_as_a_mail_of_its_own()
    {
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Message(TestNotificationModule.Eta) with { Recipients = [_bob.Id] });
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDigestJob>().ExecuteAsync(Ct);
        }

        var digest = (await MailsToAsync("bob@acme.test")).Single(m => m.TemplateName == NotificationDigestJob.Template);
        digest.HtmlBody.ShouldContain("Document shared");
        digest.HtmlBody.ShouldNotContain("Departed");
    }

    [Fact]
    public async Task Parallel_publishes_of_one_entity_leave_one_waiting_mail_and_fail_no_command()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var eta = Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment, Recipients = [_bob.Id] };

        await Task.WhenAll(Enumerable.Range(1, 8).Select(i => app.PublishAsync(_setup.AdminUserId, _setup.TenantId, eta with { Title = $"ETA {i}" }, clock)));

        (await app.InDbAsync(null, db => db.Set<PendingNotificationMail>().CountAsync(Ct))).ShouldBe(1);
        (await InboxAsync(Bob)).Count.ShouldBe(8);
    }

    [Fact]
    public async Task The_mail_job_does_not_send_a_mail_a_publisher_replaced_while_it_ran()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var eta = Message(TestNotificationModule.Eta) with { EntityType = "Shipments", EntityId = _shipment, Recipients = [_bob.Id] };
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, eta with { Title = "ETA 1 May" }, clock);
        var seen = await app.InDbAsync(null, db => db.Set<PendingNotificationMail>().AsNoTracking().SingleAsync(Ct));

        clock.Advance(TimeSpan.FromSeconds(30));
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, eta with { Title = "ETA 2 May" }, clock);
        (await app.ClaimAsync(seen, clock)).ShouldBeFalse();
        (await app.InDbAsync(null, db => db.Set<PendingNotificationMail>().SingleAsync(Ct))).Title.ShouldBe("ETA 2 May");

        clock.Advance(TimeSpan.FromMinutes(1));
        await app.RunMailJobAsync(clock);
        (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem().Subject.ShouldBe("ETA 2 May");
        (await app.InDbAsync(null, db => db.Set<PendingNotificationMail>().CountAsync(Ct))).ShouldBe(0);
    }

    private NotificationMessage Message(string type) => new(type, "Departed", "The shipment left the port.") { Link = $"/shipments/{_shipment}" };

    private static async Task<IReadOnlyList<NotificationDto>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items;

    private Task<List<OutgoingMail>> MailsToAsync(string to) =>
        app.InDbAsync(null, db => db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.To == to).ToListAsync(Ct));

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var user = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            var role = await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("R " + email, null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
            (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }
}
