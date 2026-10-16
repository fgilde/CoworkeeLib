using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Webhooks;
using Coworkee.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Webhooks.Tests;

public sealed class WebhookDeliveryTests(WebhooksApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Entity_changes_arrive_signed_with_the_columns_of_the_set()
    {
        var secret = await SubscribeAsync(Admin, "gadgets.*");

        var gadget = await AddGadgetAsync("Lamp");
        await AddGadgetAsync("secret");
        await app.SettleAsync();

        var received = app.Received.ShouldHaveSingleItem();
        var headers = received.Headers;
        WebhookSignature.Verify(secret, headers["webhook-id"], headers["webhook-timestamp"], received.Body, headers["webhook-signature"], app.Clock.GetUtcNow(), TimeSpan.FromMinutes(5))
            .ShouldBeTrue();
        var body = JsonDocument.Parse(received.Body).RootElement;
        (body.GetProperty("type").GetString(), body.GetProperty("tenant").GetGuid(), body.GetProperty("id").GetString()).ShouldBe(("gadgets.created", _setup.TenantId, headers["webhook-id"]));
        var data = body.GetProperty("data");
        (data.GetProperty("id").GetGuid(), data.GetProperty("name").GetString(), data.GetProperty("price").GetDecimal()).ShouldBe((gadget, "Lamp", 10m));
        data.TryGetProperty("cost", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_rotated_secret_signs_next_to_the_old_one_until_the_grace_period_ends()
    {
        var old = await SubscribeAsync(Admin, "gadgets.*");
        var id = (await WebhooksAsync()).Single().Id;

        var rotated = await ReadAsync<WebhookSecretDto>(await Admin.PostAsync($"/api/v1/webhooks/{id}/rotate-secret", null, Ct));
        await AddGadgetAsync("Lamp");
        await app.SettleAsync();

        rotated.Secret.ShouldNotBe(old);
        (await WebhooksAsync()).Single().PreviousSecretUntil.ShouldBe(app.Clock.GetUtcNow().AddHours(24));
        var during = app.Received.ShouldHaveSingleItem();
        during.Headers["webhook-signature"].Split(' ').Length.ShouldBe(2);
        Verifies(old, during).ShouldBeTrue();
        Verifies(rotated.Secret, during).ShouldBeTrue();

        app.Received.Clear();
        app.Clock.Advance(TimeSpan.FromHours(25));
        await AddGadgetAsync("Desk");
        await app.SettleAsync();

        var after = app.Received.ShouldHaveSingleItem();
        Verifies(old, after).ShouldBeFalse();
        Verifies(rotated.Secret, after).ShouldBeTrue();
    }

    private bool Verifies(string secret, ReceivedWebhook received) =>
        WebhookSignature.Verify(secret, received.Headers["webhook-id"], received.Headers["webhook-timestamp"], received.Body, received.Headers["webhook-signature"],
            app.Clock.GetUtcNow(), TimeSpan.FromMinutes(5));

    [Fact]
    public async Task The_data_is_what_the_owner_of_the_endpoint_may_read()
    {
        var viewer = await CreateUserAsync("viewer@acme.test", WebhookPermissions.Manage, GadgetPermissions.View);
        var blind = await CreateUserAsync("blind@acme.test", WebhookPermissions.Manage);
        await SubscribeAsync(app.As(viewer, _setup.TenantId), "gadgets.created", "?viewer");
        await SubscribeAsync(app.As(blind, _setup.TenantId), "gadgets.created", "?blind");

        await AddGadgetAsync("Lamp");
        await app.SettleAsync();

        var received = app.Received.ShouldHaveSingleItem();
        received.Query.ShouldBe("?viewer");
        var data = JsonDocument.Parse(received.Body).RootElement.GetProperty("data");
        data.GetProperty("name").GetString().ShouldBe("Lamp");
        data.TryGetProperty("price", out _).ShouldBeFalse();
        (await app.As(viewer, _setup.TenantId).GetAsync("/api/v1/webhooks", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.As(await CreateUserAsync("nobody@acme.test"), _setup.TenantId).GetAsync("/api/v1/webhooks", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Failed_deliveries_back_off_and_switch_the_endpoint_off_after_repeated_failures()
    {
        app.ReceiverStatus = 500;
        await SubscribeAsync(Admin, "order.shipped");

        await PublishAsync("order.shipped", new { order = 1 });
        await app.SettleAsync();
        var first = await DeliveriesAsync();
        (first[0].Attempts, first[0].Status, first[0].NextAttemptAt).ShouldBe((1, WebhookDeliveryStatus.Pending, app.Clock.GetUtcNow().AddSeconds(30)));
        first[0].Error.ShouldBe("The endpoint answered 500.");
        await RetryAllAsync();

        (await DeliveriesAsync())[0].Status.ShouldBe(WebhookDeliveryStatus.Failed);
        app.Jobs.Delays.ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)]);
        app.Received.Count.ShouldBe(3);
        (await WebhooksAsync())[0].IsActive.ShouldBeTrue();

        await PublishAsync("order.shipped", new { order = 2 });
        await app.SettleAsync();
        await RetryAllAsync();

        var endpoint = (await WebhooksAsync())[0];
        (endpoint.IsActive, endpoint.ConsecutiveFailures, endpoint.DisabledAt is null).ShouldBe((false, 2, false));
        var notifications = await app.WithDbAsync(null, _setup.TenantId, db => db.Set<Notification>().IgnoreQueryFilters().ToListAsync(Ct));
        notifications.ShouldHaveSingleItem().UserId.ShouldBe(_setup.AdminUserId);
        notifications[0].Type.ShouldBe(WebhookAdmins.NotificationType);

        await PublishAsync("order.shipped", new { order = 3 });
        await app.SettleAsync();
        app.Received.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Test_events_and_redeliveries_are_logged_without_the_signature()
    {
        await SubscribeAsync(Admin, "order.shipped");
        var id = (await Admin.GetFromJsonAsync<WebhookSubscriptionDto[]>("/api/v1/webhooks", Ct))!.Single().Id;

        (await Admin.PostAsync($"/api/v1/webhooks/{id}/test", null, Ct)).EnsureSuccessStatusCode();
        await app.SettleAsync();

        var test = (await DeliveriesAsync()).ShouldHaveSingleItem();
        (test.EventType, test.Status, test.ResponseStatus, test.Attempts).ShouldBe((WebhookEventTypes.Test, WebhookDeliveryStatus.Succeeded, 200, 1));
        test.Request.ShouldContain("webhook-signature: v1,***");
        test.Request.ShouldNotContain(app.Received.Single().Headers["webhook-signature"]);
        test.Response.ShouldEndWith("thanks");

        (await Admin.PostAsync($"/api/v1/webhooks/deliveries/{test.Id}/redeliver", null, Ct)).EnsureSuccessStatusCode();
        await app.SettleAsync();

        app.Received.Select(r => r.Headers["webhook-id"]).Distinct().ShouldHaveSingleItem().ShouldBe(test.EventId.ToString());
        app.Received.Select(r => r.Body).Distinct().Count().ShouldBe(1);
        (await DeliveriesAsync()).Length.ShouldBe(2);
    }

    [Fact]
    public async Task App_events_go_out_as_published_and_the_catalog_lists_every_event()
    {
        await SubscribeAsync(Admin, "*");

        await PublishAsync("order.shipped", new { orderId = 42, carrier = "DHL" });
        await app.SettleAsync();

        var body = JsonDocument.Parse(app.Received.ShouldHaveSingleItem().Body).RootElement;
        body.GetProperty("type").GetString().ShouldBe("order.shipped");
        body.GetProperty("data").GetProperty("carrier").GetString().ShouldBe("DHL");
        var types = await Admin.GetFromJsonAsync<WebhookEventTypeDto[]>("/api/v1/webhooks/event-types", Ct);
        types!.Select(t => t.Name).ShouldBe(["gadgets.created", "gadgets.deleted", "gadgets.updated", "order.shipped", "workflow.transitioned"]);
    }

    [Fact]
    public async Task Endpoints_into_disallowed_ports_or_without_events_are_refused()
    {
        (await Admin.PostAsJsonAsync("/api/v1/webhooks", new WebhookRequest("http://127.0.0.1:1/hook", ["*"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/webhooks", new WebhookRequest(app.ReceiverUrl, []), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task RetryAllAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(5));
            await app.SettleAsync();
        }
    }

    private async Task<string> SubscribeAsync(HttpClient client, string events, string query = "")
    {
        var response = await client.PostAsJsonAsync("/api/v1/webhooks", new WebhookRequest(app.ReceiverUrl + query, [events], "test"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WebhookSecretDto>(Ct))!.Secret;
    }

    private async Task<WebhookDeliveryDto[]> DeliveriesAsync() => (await Admin.GetFromJsonAsync<WebhookDeliveryDto[]>("/api/v1/webhooks/deliveries", Ct))!;

    private async Task<WebhookSubscriptionDto[]> WebhooksAsync() => (await Admin.GetFromJsonAsync<WebhookSubscriptionDto[]>("/api/v1/webhooks", Ct))!;

    private Task<Guid> AddGadgetAsync(string name) =>
        app.WithDbAsync(_setup.AdminUserId, _setup.TenantId, async db =>
        {
            var gadget = new Gadget { Name = name, Price = 10, Cost = 3, TenantId = _setup.TenantId };
            db.Add(gadget);
            await db.SaveChangesAsync(Ct);
            return gadget.Id;
        });

    private async Task PublishAsync(string type, object data)
    {
        using var actor = Core.Security.CurrentUserScope.Begin(new Core.Security.ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWebhookPublisher>().PublishAsync(type, data, Ct);
        await scope.ServiceProvider.GetRequiredService<WebhooksTestDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<Guid> CreateUserAsync(string email, params string[] permissions)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        if (permissions.Length > 0)
        {
            var role = await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest($"Role {email}", null), Ct));
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user.Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
