using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;

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
}
