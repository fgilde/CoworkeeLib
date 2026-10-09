using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Settings;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Notifications.Tests;

public sealed class NotificationTests(NotificationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null));
        _bob = (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    [Fact]
    public async Task The_daily_digest_mails_unread_notifications_once_and_respects_the_opt_out()
    {
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);

        await RunDigestAsync();
        var mails = await MailsToAsync("bob@acme.test");
        mails.Count.ShouldBe(1);
        mails[0].TemplateName.ShouldBe(NotificationDigestJob.Template);
        mails[0].Subject.ShouldContain("2");
        mails[0].HtmlBody.ShouldContain("https://app.test/d/1");

        // nothing new: no second mail
        await RunDigestAsync();
        (await MailsToAsync("bob@acme.test")).Count.ShouldBe(1);

        (await Bob.PutAsJsonAsync("/api/v1/settings/user", new SetSettingsRequest(new Dictionary<string, string?> { [NotificationDigestJob.Setting] = "False" }), Ct)).EnsureSuccessStatusCode();
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        await RunDigestAsync();
        (await MailsToAsync("bob@acme.test")).Count.ShouldBe(1);
    }

    private async Task RunDigestAsync()
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDigestJob>().ExecuteAsync(Ct);
    }

    private async Task<List<OutgoingMail>> MailsToAsync(string to)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.To == to).ToListAsync(Ct);
    }

    [Fact]
    public async Task Notify_stores_and_pushes_to_the_user_topic()
    {
        var (connection, events) = await app.ConnectAsync(_bob.Id, _setup.TenantId);
        await using var _ = connection;

        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (events.IsEmpty)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline);
            await Task.Delay(100, Ct);
        }

        events.Single().Topic.ShouldBe($"user:{_bob.Id}");
        var list = await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct);
        list!.Items.Single().Title.ShouldBe("Document shared");
    }

    [Fact]
    public async Task Localized_notifications_keep_the_text_and_its_arguments_and_the_digest_fills_them()
    {
        using (Core.Security.CurrentUserScope.Begin(new Core.Security.ImpersonatedUser(_setup.AdminUserId, _setup.TenantId)))
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyLocalizedAsync([_bob.Id], "test", "New registration", "{0} ({1}) waits for activation.",
                ["Nia New", "nia@acme.test"], "/admin/users/1", Ct);
            await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().SaveChangesAsync(Ct);
        }

        var item = (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items.Single();
        (item.Title, item.Body).ShouldBe(("New registration", "{0} ({1}) waits for activation."));
        item.Arguments.ShouldBe(["Nia New", "nia@acme.test"]);

        await RunDigestAsync();
        (await MailsToAsync("bob@acme.test")).Single().HtmlBody.ShouldContain("Nia New (nia@acme.test) waits for activation.");
    }

    [Fact]
    public async Task The_digest_shows_localized_notifications_in_each_readers_language()
    {
        (await Bob.PutAsJsonAsync("/api/v1/settings/user", new SetSettingsRequest(new Dictionary<string, string?> { [Contracts.Localization.LocalizationSettings.Culture] = "de" }), Ct))
            .EnsureSuccessStatusCode();
        using (Core.Security.CurrentUserScope.Begin(new Core.Security.ImpersonatedUser(_setup.AdminUserId, _setup.TenantId)))
        {
            await using var scope = app.App.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyLocalizedAsync([_bob.Id, _setup.AdminUserId], "test", "New registration", "{0} ({1}) waits for activation.",
                ["Nia New", "nia@acme.test"], "/admin/users/1", Ct);
            await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().SaveChangesAsync(Ct);
        }

        await RunDigestAsync();

        var german = (await MailsToAsync("bob@acme.test")).Single();
        german.HtmlBody.ShouldContain("Neue Registrierung");
        german.HtmlBody.ShouldContain("Nia New (nia@acme.test) wartet auf Freischaltung.");
        (await MailsToAsync("admin@acme.test")).Single().HtmlBody.ShouldContain("Nia New (nia@acme.test) waits for activation.");
    }

    [Fact]
    public async Task Push_reaches_the_user_when_the_sender_has_no_tenant()
    {
        var (connection, events) = await app.ConnectAsync(_bob.Id, _setup.TenantId);
        await using var _ = connection;

        await app.NotifyAsync(null, null, _bob.Id);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (events.IsEmpty)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline);
            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task List_and_unread_count_are_per_user()
    {
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);

        (await Bob.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(2);
        (await Admin.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(0);
        (await Admin.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Marking_a_foreign_notification_is_not_found()
    {
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        var notification = (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items.Single();

        (await Admin.PostAsync($"/api/v1/notifications/{notification.Id}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Bob.PostAsync($"/api/v1/notifications/{notification.Id}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Bob.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(0);
        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?unreadOnly=true", Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_all_marks_only_own_notifications()
    {
        await app.NotifyAsync(_bob.Id, _setup.TenantId, _bob.Id, _setup.AdminUserId);

        (await Bob.PostAsync("/api/v1/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Bob.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(0);
        (await Admin.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_notification_can_be_marked_unread_again_and_deleted_only_by_its_owner()
    {
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        var id = (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items.Single().Id;
        (await Bob.PostAsync($"/api/v1/notifications/{id}/read", null, Ct)).EnsureSuccessStatusCode();

        (await Bob.PostAsync($"/api/v1/notifications/{id}/unread", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Bob.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(1);

        (await Admin.DeleteAsync($"/api/v1/notifications/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Bob.DeleteAsync($"/api/v1/notifications/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_all_clears_only_own_notifications_and_the_list_filters_by_type()
    {
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id, _setup.AdminUserId);
        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);

        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?type=test", Ct))!.TotalCount.ShouldBe(2);
        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?type=other", Ct))!.Items.ShouldBeEmpty();

        (await Bob.DeleteAsync("/api/v1/notifications", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Bob.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", Ct))!.Items.ShouldBeEmpty();
        (await Admin.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count.ShouldBe(1);
    }
}
