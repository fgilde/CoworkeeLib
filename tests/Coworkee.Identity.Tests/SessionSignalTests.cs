using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Persistence;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace Coworkee.Identity.Tests;

/// <summary>Two API instances behind one Redis: a stamp change on one reaches the cached stamp of the other at once.</summary>
public sealed class SessionSignalTests(IdentityApp app) : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private SetupResultDto _setup = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task A_stamp_change_on_one_instance_drops_the_cached_stamp_on_the_other()
    {
        await using var first = await InstanceAsync();
        await using var second = await InstanceAsync();
        var before = await second.GetRequiredService<SessionStamps>().GetAsync(_setup.AdminUserId, Ct);

        var after = await app.InDbAsync(null, async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == _setup.AdminUserId, Ct);
            user.SecurityStamp = Guid.NewGuid().ToString();
            return user.SecurityStamp;
        });
        (await second.GetRequiredService<SessionStamps>().GetAsync(_setup.AdminUserId, Ct)).ShouldBe(before);

        await first.GetRequiredService<SessionSignal>().PublishAsync([new SessionChange(_setup.AdminUserId, "signed-out")], Ct);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await second.GetRequiredService<SessionStamps>().GetAsync(_setup.AdminUserId, Ct)).SecurityStamp != after)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline);
            await Task.Delay(100, Ct);
        }
    }

    private async Task<ServiceProvider> InstanceAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:redis"] = _redis.GetConnectionString(),
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        services.AddHybridCache();
        services.AddCoworkeeRedis(configuration);
        services.AddSingleton<IModelContributor, IdentityModelContributor>();
        services.AddCoworkeeDbContext<IdentityTestDbContext>((_, options) => options.UseNpgsql(app.ConnectionString));
        services.AddSingleton<SessionStamps>();
        services.AddSingleton<SessionSignal>();
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SessionSignal>().StartAsync(Ct);
        return provider;
    }
}
