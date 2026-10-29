using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Outbox;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
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
    public async Task The_end_the_auth_server_wrote_to_the_outbox_reaches_the_targets_user_topic()
    {
        var bob = Guid.CreateVersion7();
        await using var listener = await app.ConnectAsync(bob, _setup.TenantId);
        await listener.SubscribeAsync(RealtimeTopics.User(bob));
        await using var other = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await other.SubscribeAsync(RealtimeTopics.User(_setup.AdminUserId));

        await app.AsActorAsync(null, null, async db =>
        {
            db.Add(new ImpersonationEnded(bob, _setup.AdminUserId).ToOutbox(DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });
        await new OutboxProcessor<RealtimeTestDbContext>(app.App.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<OutboxProcessor<RealtimeTestDbContext>>.Instance).ProcessBatchAsync(10, Ct);

        var envelope = await listener.NextAsync(e => e.Type == RealtimeEventTypes.ImpersonationEnded);
        envelope.Topic.ShouldBe(RealtimeTopics.User(bob));
        envelope.Payload.GetProperty("targetId").GetGuid().ShouldBe(bob);
        envelope.Payload.GetProperty("impersonatorId").GetGuid().ShouldBe(_setup.AdminUserId);
        other.Events.ShouldNotContain(e => e.Type == RealtimeEventTypes.ImpersonationEnded);
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
