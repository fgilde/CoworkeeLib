using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Realtime.Tests;

public sealed class KeptSessionTests(RealtimeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_password_names_the_session_that_goes_on_and_ends_the_others()
    {
        var bob = await app.AsActorAsync(null, null, async db =>
        {
            var user = new User { TenantId = _setup.TenantId, UserName = "bob@acme.test", NormalizedUserName = "BOB@ACME.TEST", Email = "bob@acme.test", SecurityStamp = "old" };
            db.Add(user);
            await db.SaveChangesAsync(Ct);
            return user.Id;
        });
        var stamp = $"{SessionStamp.ClaimType}={SessionStamp.Hash("old")}";
        await using var mine = await app.ConnectAsync(bob, _setup.TenantId, claims: [stamp, "sid=mine"]);
        await using var other = await app.ConnectAsync(bob, _setup.TenantId, claims: [stamp, "sid=other"]);

        // what ChangeMyPassword saves: a new hash and stamp, and the calling session kept
        await app.AsActorAsync(null, _setup.TenantId, async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == bob, Ct);
            user.PasswordHash = "new";
            user.SecurityStamp = "new";
            user.KeptSession = SessionStamp.Keep("mine", SessionStamp.Hash("old"), "new");
            return await db.SaveChangesAsync(Ct);
        });

        var revoked = await other.NextAsync(e => e.Type == RealtimeEventTypes.SessionRevoked);
        revoked.Payload.GetProperty("reason").GetString().ShouldBe("password-changed");
        revoked.Payload.GetProperty("keptSessionId").GetString().ShouldBe("mine");
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        mine.Events.ShouldBeEmpty();
        mine.Connection.State.ShouldBe(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected);
        other.Events.Count(e => e.Type == RealtimeEventTypes.SessionRevoked).ShouldBe(1);
    }

    [Fact]
    public async Task Decorators_of_the_publisher_see_the_revocation_with_the_kept_session()
    {
        var carl = await app.AsActorAsync(null, null, async db =>
        {
            var user = new User { TenantId = _setup.TenantId, UserName = "carl@acme.test", NormalizedUserName = "CARL@ACME.TEST", Email = "carl@acme.test", SecurityStamp = "old" };
            db.Add(user);
            await db.SaveChangesAsync(Ct);
            return user.Id;
        });

        // no connection to RealtimeHub: an app's own hub still has to hear of it
        await app.AsActorAsync(null, _setup.TenantId, async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == carl, Ct);
            user.PasswordHash = "new";
            user.SecurityStamp = "new";
            user.KeptSession = SessionStamp.Keep("mine", SessionStamp.Hash("old"), "new");
            return await db.SaveChangesAsync(Ct);
        });

        var recorder = (RecordingPublisher)app.App.Services.GetRequiredService<IRealtimePublisher>();
        recorder.Published.Where(p => p.Type == RealtimeEventTypes.SessionRevoked && p.Topic == RealtimeTopics.User(carl))
            .ShouldHaveSingleItem().Payload.ShouldBe(new SessionRevokedPayload("password-changed", "mine"));
    }
}
