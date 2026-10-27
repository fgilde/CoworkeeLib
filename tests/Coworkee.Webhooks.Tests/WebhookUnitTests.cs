using System.Net;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks.Tests;

public sealed class WebhookUnitTests
{
    private static readonly WebhookUrlGuard Guard = new(Options.Create(new WebhookOptions()));

    [Fact]
    public void Signatures_match_the_example_of_the_Standard_Webhooks_spec()
    {
        const string secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

        WebhookSignature.Sign(secret, "msg_p5jXN8AQM9LWM0D4loKWxJek", 1614265330, """{"test": 2432232314}""")
            .ShouldBe("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");
    }

    [Fact]
    public void Verification_needs_the_right_secret_body_and_a_fresh_timestamp()
    {
        var secret = WebhookSignature.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds();
        var signature = WebhookSignature.Sign(secret, "id", timestamp, "{}");
        var tolerance = TimeSpan.FromMinutes(5);

        WebhookSignature.Verify(secret, "id", $"{timestamp}", "{}", $"v1,old {signature}", now, tolerance).ShouldBeTrue();
        WebhookSignature.Verify(secret, "id", $"{timestamp}", "{ }", signature, now, tolerance).ShouldBeFalse();
        WebhookSignature.Verify(WebhookSignature.NewSecret(), "id", $"{timestamp}", "{}", signature, now, tolerance).ShouldBeFalse();
        WebhookSignature.Verify(secret, "id", $"{timestamp}", "{}", signature, now.AddMinutes(6), tolerance).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://hooks.example.com/in", null)]
    [InlineData("http://hooks.example.com/in", "Webhooks need https.")]
    [InlineData("ftp://hooks.example.com/in", "Enter an absolute https address.")]
    [InlineData("/relative", "Enter an absolute https address.")]
    [InlineData("https://user:pw@hooks.example.com/in", "The address may not contain a user name or password.")]
    [InlineData("https://hooks.example.com:8443/in", "Only the standard port is allowed.")]
    [InlineData("https://localhost/in", "The address points into a private network.")]
    [InlineData("https://127.0.0.1/in", "The address points into a private network.")]
    [InlineData("https://10.1.2.3/in", "The address points into a private network.")]
    [InlineData("https://169.254.169.254/latest/meta-data", "The address points into a private network.")]
    [InlineData("https://[::1]/in", "The address points into a private network.")]
    [InlineData("https://[fd00::1]/in", "The address points into a private network.")]
    public void Addresses_are_checked_before_they_are_saved(string url, string? problem) => Guard.Check(url).ShouldBe(problem);

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2002:a00:1::", false)]
    public void Only_public_addresses_count_as_public(string address, bool isPublic) => WebhookUrlGuard.IsPublic(IPAddress.Parse(address)).ShouldBe(isPublic);

    [Fact]
    public async Task Sending_never_connects_to_a_name_that_resolves_into_the_private_network()
    {
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = Guard.ConnectAsync, UseProxy = false });

        var failure = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("https://localhost/in", TestContext.Current.CancellationToken));

        failure.InnerException.ShouldBeOfType<WebhookBlockedException>();
    }

    [Fact]
    public void Messages_of_anonymous_records_carry_no_actor_and_no_trace()
    {
        using var request = new System.Diagnostics.Activity("request").Start();
        var (user, impersonator) = (Guid.NewGuid(), Guid.NewGuid());
        var actor = new Core.Security.ImpersonatedUser(user, null, impersonator);

        var named = WebhookOutbox.Message(new WebhookEntityChanged("Ballots", "1", WebhookNames.Created), null, actor, TimeProvider.System);
        var anonymous = WebhookOutbox.Message(new WebhookEntityChanged("Ballots", "1", WebhookNames.Created), null, actor, TimeProvider.System, anonymous: true);

        (named.ActorId, named.ImpersonatorId, named.CorrelationId is not null).ShouldBe((user, impersonator, true));
        (anonymous.ActorId, anonymous.ImpersonatorId, anonymous.CorrelationId).ShouldBe((null, null, null));
    }

    [Fact]
    public void Retries_wait_twice_as_long_each_time_up_to_the_cap()
    {
        var options = new WebhookOptions { FirstRetryDelay = TimeSpan.FromSeconds(30), MaxRetryDelay = TimeSpan.FromMinutes(3) };

        Enumerable.Range(1, 5).Select(attempt => WebhookBackoff.Delay(attempt, options).TotalSeconds).ShouldBe([30, 60, 120, 180, 180]);
        WebhookBackoff.Delay(200, options).ShouldBe(TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData("*", "gadgets.created", true)]
    [InlineData("gadgets.*", "gadgets.deleted", true)]
    [InlineData("gadgets.*", "gadget_parts.created", false)]
    [InlineData("gadgets.created", "gadgets.updated", false)]
    [InlineData("order.shipped", "webhook.test", true)]
    public void Endpoints_take_the_events_they_chose(string pattern, string eventType, bool wanted) =>
        new WebhookSubscription { Url = "https://x.test", ProtectedSecret = "x", Events = [pattern] }.Wants(eventType).ShouldBe(wanted);
}
