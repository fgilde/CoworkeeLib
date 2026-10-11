using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace Coworkee.Realtime.Tests;

/// <summary>Two instances of the app behind one Redis: events, presence and cache removals cross between them.</summary>
public sealed class RedisScaleOutTests(RealtimeApp app) : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private SetupResultDto _setup = null!;
    private WebApplication _first = null!;
    private WebApplication _second = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        _setup = await app.SetupAsync();
        _first = await app.StartInstanceAsync(_redis.GetConnectionString());
        _second = await app.StartInstanceAsync(_redis.GetConnectionString());
        foreach (var instance in new[] { _first, _second })
        {
            instance.Services.GetRequiredService<RealtimePresence>().Grace = TimeSpan.FromMilliseconds(300);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task A_user_counts_once_across_instances_and_events_reach_the_other_instance()
    {
        var bob = (await (await _first.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId)
            .PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        await using var admin = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId, on: _first);
        await admin.SubscribeAsync(PresenceEvents.Topic);

        var onSecond = await app.ConnectAsync(bob.Id, _setup.TenantId, on: _second);
        await admin.NextAsync(e => Is(e, bob.Id, true));
        (await OnlineAsync(_first)).ShouldContain(bob.Id);

        var onFirst = await app.ConnectAsync(bob.Id, _setup.TenantId, on: _first);
        await onSecond.DisposeAsync();
        await Task.Delay(1000, Ct);
        admin.Events.ShouldNotContain(e => Is(e, bob.Id, false));
        admin.Events.Count(e => Is(e, bob.Id, true)).ShouldBe(1);
        (await OnlineAsync(_second)).ShouldContain(bob.Id);

        await onFirst.DisposeAsync();
        await admin.NextAsync(e => Is(e, bob.Id, false));
        (await OnlineAsync(_second)).ShouldNotContain(bob.Id);
    }

    [Fact]
    public async Task A_cache_removal_on_one_instance_drops_the_entry_on_the_other()
    {
        var first = _first.Services.GetRequiredService<HybridCache>();
        var second = _second.Services.GetRequiredService<HybridCache>();
        (await first.GetOrCreateAsync("k", _ => ValueTask.FromResult(1), tags: ["t"], cancellationToken: Ct)).ShouldBe(1);
        (await second.GetOrCreateAsync("k", _ => ValueTask.FromResult(1), tags: ["t"], cancellationToken: Ct)).ShouldBe(1);

        await first.RemoveByTagAsync("t", Ct);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await second.GetOrCreateAsync("k", _ => ValueTask.FromResult(2), tags: ["t"], cancellationToken: Ct) != 2)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline);
            await Task.Delay(100, Ct);
        }
    }

    private static bool Is(RealtimeEnvelope envelope, Guid userId, bool online) =>
        envelope.Type == PresenceEvents.Changed && envelope.Payload.GetProperty("userId").GetGuid() == userId && envelope.Payload.GetProperty("online").GetBoolean() == online;

    private async Task<Guid[]> OnlineAsync(WebApplication instance) =>
        (await instance.GetTestClient().AsUser(_setup.AdminUserId, _setup.TenantId).GetFromJsonAsync<Guid[]>(PresenceEvents.Path, Ct))!;
}
