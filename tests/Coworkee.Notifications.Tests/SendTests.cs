using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Events;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Mailing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Notifications.Tests;

public sealed class SendTests(NotificationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;
    private UserDto _carl = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await CreateUserAsync("bob@acme.test", Shipment.ViewShipments);
        _carl = await CreateUserAsync("carl@acme.test");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_toast_reaches_the_online_users_of_its_target_and_writes_no_row_and_no_mail()
    {
        var (connection, events) = await app.ConnectAsync(_bob.Id, _setup.TenantId);
        await using var _ = connection;

        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, new NotificationRequest(EventTarget.Permission(Shipment.ViewShipments), "Maintenance at {0}")
        {
            Delivery = Delivery.Toast,
            Severity = NotificationSeverity.Warning,
            Arguments = ["18:00"],
            Link = "/status",
            Mail = MailDelivery.Never,
        });

        var toast = await NextAsync(events, e => e.Type == ToastPayload.Event);
        toast.Topic.ShouldBe(RealtimeTopics.User(_bob.Id));
        var payload = toast.Payload.Deserialize<ToastPayload>(JsonSerializerOptions.Web)!;
        (payload.Title, payload.Severity, payload.Link, payload.Type).ShouldBe(("Maintenance at {0}", NotificationSeverity.Warning, "/status", NotificationRequest.DefaultType));
        payload.Arguments.ShouldBe(["18:00"]);
        toast.Actor!.UserId.ShouldBe(_setup.AdminUserId);
        (await app.InDbAsync(null, db => db.Set<Notification>().CountAsync(Ct))).ShouldBe(0);
        (await app.InDbAsync(null, db => db.Set<OutgoingMail>().IgnoreQueryFilters().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task The_toast_shorthand_reaches_the_whole_organisation()
    {
        var (connection, events) = await app.ConnectAsync(_carl.Id, _setup.TenantId);
        await using var _ = connection;
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, RealtimeTopics.TenantEvents, Ct);

        await ToastAsync(EventTarget.All, "Maintenance at 18:00", NotificationSeverity.Warning);

        var toast = await NextAsync(events, e => e.Type == ToastPayload.Event);
        toast.Topic.ShouldBe(RealtimeTopics.TenantEvents);
        toast.Payload.Deserialize<ToastPayload>(JsonSerializerOptions.Web)!.Severity.ShouldBe(NotificationSeverity.Warning);
        (await app.InDbAsync(null, db => db.Set<Notification>().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task A_stored_notification_to_a_permission_reaches_its_holders_but_not_the_actor()
    {
        await app.SendAsync(_bob.Id, _setup.TenantId, new NotificationRequest(EventTarget.Permission(Shipment.ViewShipments), "Departed"));

        (await RecipientsAsync()).ShouldBe([_setup.AdminUserId]);
    }

    [Fact]
    public async Task Users_named_by_id_get_it_even_as_the_actor_as_with_notify()
    {
        await app.SendAsync(_bob.Id, _setup.TenantId, new NotificationRequest(EventTarget.Users(_bob.Id, _carl.Id), "Shared") { Link = "/d/1" });

        (await RecipientsAsync()).ShouldBe([_bob.Id, _carl.Id], ignoreOrder: true);
        (await app.InDbAsync(null, db => db.Set<Notification>().Select(n => n.Type).Distinct().ToListAsync(Ct))).ShouldBe([NotificationRequest.DefaultType]);
    }

    [Fact]
    public async Task Stored_and_toast_together_store_the_row_and_toast_once_it_is_saved()
    {
        var (connection, events) = await app.ConnectAsync(_carl.Id, _setup.TenantId);
        await using var _ = connection;

        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, new NotificationRequest(EventTarget.Users(_carl.Id), "Approved") { Delivery = Delivery.Stored | Delivery.Toast });

        await NextAsync(events, e => e.Type == ToastPayload.Event);
        (await RecipientsAsync()).ShouldBe([_carl.Id]);
    }

    [Fact]
    public async Task Mail_never_always_and_by_preference_decide_who_gets_a_mail()
    {
        // Milestone mails by preference (default on) after the type's delay; status is off by default
        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request(TestNotificationModule.Milestone) with { Mail = MailDelivery.Never });
        (await WaitingAsync()).ShouldBeEmpty();

        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request(TestNotificationModule.Milestone));
        (await WaitingAsync()).ShouldBe([_bob.Id, _carl.Id], ignoreOrder: true);

        // an unregistered type mails nobody by preference, everybody with Always
        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request("adhoc"));
        (await MailsToAsync("carl@acme.test")).ShouldBeEmpty();
        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request("adhoc") with { Mail = MailDelivery.Always });
        (await MailsToAsync("carl@acme.test")).ShouldHaveSingleItem().Subject.ShouldBe("Departed");
        (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem();

        // Off wins over Always
        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request(TestNotificationModule.Status) with { Mail = MailDelivery.Always });
        (await MailsToAsync("carl@acme.test")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Mail_when_offline_goes_to_the_users_without_a_connection_and_keeps_the_delay()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var (connection, _) = await app.ConnectAsync(_bob.Id, _setup.TenantId);
        await using var _ = connection;

        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request("adhoc") with { Mail = MailDelivery.WhenOffline }, clock);
        (await MailsToAsync("carl@acme.test")).ShouldHaveSingleItem();
        (await MailsToAsync("bob@acme.test")).ShouldBeEmpty();

        // a type with a delay: the mail waits as before
        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, Request(TestNotificationModule.Eta) with { Mail = MailDelivery.WhenOffline }, clock);
        (await WaitingAsync()).ShouldBe([_carl.Id]);
        clock.Advance(TimeSpan.FromMinutes(2));
        await app.RunMailJobAsync(clock);
        (await MailsToAsync("carl@acme.test")).Count.ShouldBe(2);
        (await MailsToAsync("bob@acme.test")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_decorator_written_before_send_passes_stored_requests_to_user_ids_on_and_refuses_the_rest()
    {
        var inner = new RecordingNotifier();
        INotifier decorator = new OldDecorator(inner);

        await decorator.SendAsync(new NotificationRequest(EventTarget.Users(_bob.Id), "Shared") { Link = "/d/1" }, Ct);

        var message = inner.Published.ShouldHaveSingleItem();
        (message.Title, message.Link, message.Type).ShouldBe(("Shared", "/d/1", NotificationRequest.DefaultType));
        message.Recipients.ShouldBe([_bob.Id]);
        await Should.ThrowAsync<NotSupportedException>(() => decorator.ToastAsync(EventTarget.All, "Hello", cancellationToken: Ct));
    }

    private NotificationRequest Request(string type) =>
        new(EventTarget.Users(_bob.Id, _carl.Id), "Departed", "The shipment left the port.") { Type = type };

    private async Task ToastAsync(EventTarget target, string title, NotificationSeverity severity)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotifier>().ToastAsync(target, title, severity, Ct);
    }

    private static async Task<RealtimeEnvelope> NextAsync(ConcurrentQueue<RealtimeEnvelope> events, Func<RealtimeEnvelope, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (events.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("No matching realtime event.");
    }

    private Task<List<Guid>> RecipientsAsync() => app.InDbAsync(null, db => db.Set<Notification>().IgnoreQueryFilters().Select(n => n.UserId).ToListAsync(Ct));

    private Task<List<Guid>> WaitingAsync() => app.InDbAsync(null, db => db.Set<PendingNotificationMail>().Select(m => m.UserId).ToListAsync(Ct));

    private Task<List<OutgoingMail>> MailsToAsync(string to) =>
        app.InDbAsync(null, db => db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.To == to).ToListAsync(Ct));

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var user = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{user.Id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }

    private sealed class RecordingNotifier : INotifier
    {
        public List<NotificationMessage> Published { get; } = [];

        public Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task NotifyLocalizedAsync(
            IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>An app's decorator from before SendAsync (like a filter of comment notifications).</summary>
    private sealed class OldDecorator(INotifier inner) : INotifier
    {
        public Task NotifyAsync(IEnumerable<Guid> userIds, string type, string title, string? body = null, string? link = null, CancellationToken cancellationToken = default) =>
            inner.NotifyAsync(userIds, type, title, body, link, cancellationToken);

        public Task NotifyLocalizedAsync(
            IEnumerable<Guid> userIds, string type, string title, string? body, IReadOnlyList<string> arguments, string? link = null, CancellationToken cancellationToken = default) =>
            inner.NotifyLocalizedAsync(userIds, type, title, body, arguments, link, cancellationToken);

        public Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default) => inner.PublishAsync(message, cancellationToken);
    }
}
