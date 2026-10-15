using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Notifications.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Notifications.Tests;

public sealed class PushTests(NotificationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private UserDto _bob = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null));
        _bob = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        app.PushSender.Sent.Clear();
        app.PushSender.Answer = PushResult.Sent;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private HttpClient Bob => app.As(_bob.Id, _setup.TenantId);

    [Fact]
    public async Task A_device_subscribes_and_receives_the_users_notifications_in_his_language_with_their_link()
    {
        var settings = await Bob.GetFromJsonAsync<PushSettingsDto>("/api/v1/notifications/push", Ct);
        settings!.PublicKey.ShouldNotBeNullOrEmpty();
        settings.Devices.ShouldBeEmpty();

        var device = await SubscribeAsync(Bob, "https://push.example.com/send/bob-phone");
        device.Device.ShouldBe("Chrome · Android");
        (await Bob.GetFromJsonAsync<PushSettingsDto>("/api/v1/notifications/push", Ct))!.Devices.Single().Id.ShouldBe(device.Id);

        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        var notification = await DeliverLatestAsync();

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().Set<OutboxMessage>()
                .AnyAsync(m => m.Type.Contains(nameof(NotificationCreated)) && m.Payload.Contains(notification.ToString()), Ct)).ShouldBeTrue();
        }

        var (sentTo, payload) = app.PushSender.Sent.Single();
        sentTo.Endpoint.ShouldBe("https://push.example.com/send/bob-phone");
        var json = JsonDocument.Parse(payload).RootElement;
        json.GetProperty("title").GetString().ShouldBe("Document shared");
        json.GetProperty("body").GetString().ShouldBe("Grace shared a file.");
        json.GetProperty("url").GetString().ShouldBe("/d/1");
        json.GetProperty("tag").GetGuid().ShouldBe(notification);
    }

    [Fact]
    public async Task A_device_the_push_service_no_longer_knows_is_removed()
    {
        await SubscribeAsync(Bob, "https://push.example.com/send/gone");
        app.PushSender.Answer = PushResult.Gone;

        await app.NotifyAsync(_setup.AdminUserId, _setup.TenantId, _bob.Id);
        await DeliverLatestAsync();

        (await Bob.GetFromJsonAsync<PushSettingsDto>("/api/v1/notifications/push", Ct))!.Devices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_owner_removes_a_device_and_the_browser_moves_with_the_account_that_signs_in()
    {
        var device = await SubscribeAsync(Bob, "https://push.example.com/send/shared-laptop");

        (await Admin.DeleteAsync($"/api/v1/notifications/push/{device.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await SubscribeAsync(Admin, "https://push.example.com/send/shared-laptop");
        (await Bob.GetFromJsonAsync<PushSettingsDto>("/api/v1/notifications/push", Ct))!.Devices.ShouldBeEmpty();
        (await Admin.DeleteAsync($"/api/v1/notifications/push/{device.Id}", Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await Admin.GetFromJsonAsync<PushSettingsDto>("/api/v1/notifications/push", Ct))!.Devices.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://push.example.com/send/1")]
    [InlineData("https://localhost/send/1")]
    [InlineData("https://127.0.0.1/send/1")]
    [InlineData("https://[::1]/send/1")]
    [InlineData("https://intranet/send/1")]
    [InlineData("https://printer.local/send/1")]
    [InlineData("https://push.example.com:8443/send/1")]
    public async Task Only_push_services_on_the_internet_are_accepted_as_endpoint(string endpoint)
    {
        var (key, auth) = BrowserKeys();

        var response = await Bob.PutAsJsonAsync("/api/v1/notifications/push", new PushSubscriptionRequest(endpoint, key, auth), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void The_payload_is_sealed_so_only_the_browser_reads_it_and_the_vapid_token_is_signed_with_the_server_key()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var browserPublic = WebPushSender.Uncompressed(browser.ExportParameters(false).Q);
        var auth = RandomNumberGenerator.GetBytes(16);

        var body = WebPushSender.Encrypt(Encoding.UTF8.GetBytes("{\"title\":\"Hallo\"}"), browserPublic, auth);

        BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16)).ShouldBe(4096u);
        var senderPublic = body[21..(21 + body[20])];
        using var sender = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = WebPushSender.Point(senderPublic) });
        var (key, nonce) = WebPushSender.ContentKeys(browser.DeriveRawSecretAgreement(sender.PublicKey), auth, browserPublic, senderPublic, body[..16]);
        var sealedRecord = body[(21 + senderPublic.Length)..];
        var plain = new byte[sealedRecord.Length - 16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, sealedRecord[..^16], sealedRecord[^16..], plain);
        }

        plain[^1].ShouldBe((byte)2);
        Encoding.UTF8.GetString(plain[..^1]).ShouldBe("{\"title\":\"Hallo\"}");

        var (publicKey, privateKey) = WebPushSender.CreateKeys();
        var token = WebPushSender.VapidToken(new Uri("https://push.example.com/send/1"), "mailto:ops@example.com", publicKey, privateKey, DateTimeOffset.UtcNow).Split('.');
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = WebPushSender.Point(Base64Url.DecodeFromChars(publicKey)) });
        verifier.VerifyData(Encoding.ASCII.GetBytes($"{token[0]}.{token[1]}"), Base64Url.DecodeFromChars(token[2]), HashAlgorithmName.SHA256).ShouldBeTrue();
        JsonDocument.Parse(Base64Url.DecodeFromChars(token[1])).RootElement.GetProperty("aud").GetString().ShouldBe("https://push.example.com");
    }

    private async Task<PushDeviceDto> SubscribeAsync(HttpClient client, string endpoint)
    {
        var (key, auth) = BrowserKeys();
        var response = await client.PutAsJsonAsync("/api/v1/notifications/push", new PushSubscriptionRequest(endpoint, key, auth, "Chrome · Android"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PushDeviceDto>(Ct))!;
    }

    private static (string Key, string Auth) BrowserKeys()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return (Base64Url.EncodeToString(WebPushSender.Uncompressed(browser.ExportParameters(false).Q)), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    // what the job server does with the queued job
    private async Task<Guid> DeliverLatestAsync()
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NotificationTestDbContext>();
        var id = await db.Set<Notification>().Where(n => n.UserId == _bob.Id).OrderByDescending(n => n.CreatedAt).Select(n => n.Id).FirstAsync(Ct);
        await ActivatorUtilities.CreateInstance<PushDeliveryJob>(services, NullLogger<PushDeliveryJob>.Instance).ExecuteAsync(id, Ct);
        return id;
    }
}
