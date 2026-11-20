using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Events;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Coworkee.Identity.Domain;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Notifications.Tests;

/// <summary>User selections (filter + added - left out) resolved on the server with the caller's rights, and the Send notification command on top.</summary>
public sealed class SendNotificationTests(NotificationApp app) : IAsyncLifetime
{
    private const string Count = "/api/v1/identity/users/selection/count";
    private const string Send = "/api/v1/notifications/send";
    private const string AllOfAcme = "contains(Email,'acme.test')";
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;
    private UserDto _carl = null!;
    private UserDto _dave = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await CreateUserAsync("bob@acme.test");
        _carl = await CreateUserAsync("carl@acme.test");
        _dave = await CreateUserAsync("dave@acme.test");
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{_dave.Id}", new UpdateUserRequest(null, null, false), Ct)).EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_selection_is_the_filter_plus_the_added_minus_the_left_out_users_of_the_own_organisation()
    {
        var eve = await AddTenantWithUserAsync("eve@other.test");

        (await CountAsync(Admin, new UserSelection(AllOfAcme, Excluded: [_carl.Id]))).ShouldBe(new UserSelectionCountDto(3, 2)); // admin, bob, dave (inactive)
        (await CountAsync(Admin, new UserSelection("Email eq 'bob@acme.test'", Included: [_carl.Id]))).ShouldBe(new UserSelectionCountDto(2, 2));
        (await CountAsync(Admin, UserSelection.Of(_bob.Id, eve, Guid.NewGuid()))).ShouldBe(new UserSelectionCountDto(1, 1)); // other organisations and unknown ids drop
        (await CountAsync(Admin, new UserSelection())).ShouldBe(new UserSelectionCountDto(4, 3));
    }

    [Fact]
    public async Task A_selection_refuses_filters_the_user_may_not_run_and_empty_or_oversized_choices()
    {
        (await Admin.PostAsJsonAsync(Count, new UserSelection("PasswordHash eq 'x'"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Count, new UserSelection("Nope eq 1"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Count, new UserSelection(AllMatching: false), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Count, UserSelection.Of(Enumerable.Range(0, UserSelection.MaxIds + 1).Select(_ => Guid.NewGuid())), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Counting_needs_the_right_to_view_users_and_sending_also_the_send_permission()
    {
        var plain = app.As((await CreateUserAsync("plain@acme.test")).Id, _setup.TenantId);
        var viewer = app.As((await CreateUserAsync("viewer@acme.test", IdentityPermissions.Users.View)).Id, _setup.TenantId);
        var sender = app.As((await CreateUserAsync("sender@acme.test", NotificationPermissions.Send)).Id, _setup.TenantId);
        var request = new SendNotificationRequest(UserSelection.Of(_bob.Id), "Hello");

        (await plain.PostAsJsonAsync(Count, UserSelection.Of(_bob.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CountAsync(viewer, UserSelection.Of(_bob.Id))).Users.ShouldBe(1);
        (await plain.PostAsJsonAsync(Send, request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync(Send, request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sender.PostAsJsonAsync(Send, request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RecipientsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sending_stores_toasts_and_mails_for_the_active_users_of_the_selection_but_not_for_the_sender()
    {
        var (connection, events) = await app.ConnectAsync(_bob.Id, _setup.TenantId);
        await using var _ = connection;

        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(new UserSelection(AllOfAcme), "Release {0}", "Read the news.")
        {
            Arguments = ["2.0"],
            Link = "/news",
            Severity = NotificationSeverity.Success,
            Toast = true,
            Mail = NotificationMail.Always,
        }, Ct)).EnsureSuccessStatusCode();
        await app.ProcessOutboxAsync();

        (await RecipientsAsync()).ShouldBe([_bob.Id, _carl.Id], ignoreOrder: true); // not the sender, not the inactive dave
        var stored = await app.InDbAsync(null, db => db.Set<Notification>().IgnoreQueryFilters().FirstAsync(Ct));
        (stored.Type, stored.Title, stored.Body, stored.Link).ShouldBe((NotificationRequest.DefaultType, "Release {0}", "Read the news.", "/news"));
        stored.Arguments.ShouldBe(["2.0"]);
        var toast = (await NextAsync(events, e => e.Type == ToastPayload.Event)).Payload.Deserialize<ToastPayload>(JsonSerializerOptions.Web)!;
        (toast.Title, toast.Severity, toast.Link).ShouldBe(("Release {0}", NotificationSeverity.Success, "/news"));
        (await MailsToAsync("carl@acme.test")).ShouldHaveSingleItem();
        (await MailsToAsync("admin@acme.test")).ShouldBeEmpty();
        (await MailsToAsync("dave@acme.test")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_toast_alone_writes_no_row_and_a_type_and_mail_mode_are_taken_as_sent()
    {
        await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(UserSelection.Of(_bob.Id), "Toast only") { Stored = false, Toast = true, Mail = NotificationMail.Never }, Ct);
        (await RecipientsAsync()).ShouldBeEmpty();

        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(UserSelection.Of(_carl.Id), "Typed") { Type = "adhoc" }, Ct)).EnsureSuccessStatusCode();
        (await app.InDbAsync(null, db => db.Set<Notification>().IgnoreQueryFilters().SingleAsync(Ct))).Type.ShouldBe("adhoc");
        (await MailsToAsync("carl@acme.test")).ShouldBeEmpty(); // by preference: an unregistered type mails nobody
    }

    [Fact]
    public async Task Sending_refuses_unsafe_links_empty_texts_and_nowhere_to_show()
    {
        var bob = UserSelection.Of(_bob.Id);
        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(bob, "Click") { Link = "javascript:alert(1)" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(bob, "Click") { Link = "//evil.test" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(bob, " "), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(bob, "Nowhere") { Stored = false, Mail = NotificationMail.Never }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync(Send, new SendNotificationRequest(new UserSelection("PasswordHash eq 'x'"), "Leak"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RecipientsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_selection_target_in_code_reaches_the_active_users_drops_the_actor_and_needs_the_right_to_view_users()
    {
        // bob may not view users: the selection fails loudly instead of reaching nobody
        (await Should.ThrowAsync<InvalidOperationException>(() => app.SendAsync(_bob.Id, _setup.TenantId, new NotificationRequest(EventTarget.Users(new UserSelection(AllOfAcme)), "From code"))))
            .Message.ShouldContain("right to view users");
        (await RecipientsAsync()).ShouldBeEmpty();

        await app.SendAsync(_setup.AdminUserId, _setup.TenantId, new NotificationRequest(EventTarget.Users(new UserSelection(AllOfAcme, Excluded: [_carl.Id])), "From code"));
        (await RecipientsAsync()).ShouldBe([_bob.Id]);
    }

    private static async Task<UserSelectionCountDto> CountAsync(HttpClient client, UserSelection selection)
    {
        var response = await client.PostAsJsonAsync(Count, selection, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserSelectionCountDto>(Ct))!;
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

    private Task<List<OutgoingMail>> MailsToAsync(string to) =>
        app.InDbAsync(null, db => db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.To == to).ToListAsync(Ct));

    private Task<Guid> AddTenantWithUserAsync(string email) =>
        app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), TenantId = tenant.Id, IsActive = true };
            db.AddRange(tenant, user);
            await db.SaveChangesAsync(Ct);
            return user.Id;
        });

    private async Task<UserDto> CreateUserAsync(string email, params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{user.Id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        }

        return user;
    }
}
