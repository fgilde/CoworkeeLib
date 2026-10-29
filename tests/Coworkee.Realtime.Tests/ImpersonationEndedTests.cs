using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Outbox;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Realtime.Tests;

public sealed class ImpersonationEndedTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_end_reaches_the_impersonating_session_only_never_the_targets_own_sessions()
    {
        var bob = Guid.CreateVersion7();
        await using var impersonated = await app.ConnectAsync(bob, _setup.TenantId, claims: [$"sid=helper-session", $"{ImpersonationClaims.Impersonator}={_setup.AdminUserId}"]);
        await impersonated.SubscribeAsync(RealtimeTopics.Session("helper-session"));
        await using var own = await app.ConnectAsync(bob, _setup.TenantId, claims: ["sid=bobs-session"]);
        await own.SubscribeAsync(RealtimeTopics.User(bob));
        await own.SubscribeAsync(RealtimeTopics.Session("bobs-session"));

        await app.AsActorAsync(null, null, async db =>
        {
            db.Add(new ImpersonationEnded(bob, _setup.AdminUserId, "helper-session").ToOutbox(DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });
        await new OutboxProcessor<RealtimeTestDbContext>(app.App.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<OutboxProcessor<RealtimeTestDbContext>>.Instance).ProcessBatchAsync(10, Ct);

        var envelope = await impersonated.NextAsync(e => e.Type == RealtimeEventTypes.ImpersonationEnded);
        envelope.Topic.ShouldBe(RealtimeTopics.Session("helper-session"));
        envelope.Payload.GetProperty("targetId").GetGuid().ShouldBe(bob);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        own.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_a_token_of_the_session_follows_its_topic()
    {
        await using var other = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId, claims: ["sid=mine"]);
        await using var none = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);

        await Should.ThrowAsync<HubException>(() => other.SubscribeAsync(RealtimeTopics.Session("theirs")));
        await Should.ThrowAsync<HubException>(() => none.SubscribeAsync(RealtimeTopics.Session("theirs")));
        await Should.ThrowAsync<HubException>(() => none.SubscribeAsync(RealtimeTopics.Session(string.Empty)));
        await other.SubscribeAsync(RealtimeTopics.Session("mine"));
    }

    [Fact]
    public async Task A_browser_of_an_allowed_origin_negotiates_the_hub_with_credentials()
    {
        await using var instance = await app.StartInstanceAsync(null, new() { ["Coworkee:Cors:Origins:0"] = "https://spa.test" });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/realtime/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "https://spa.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-requested-with");

        var response = await instance.GetTestClient().SendAsync(request, Ct);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://spa.test"]);
        response.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
    }
}
