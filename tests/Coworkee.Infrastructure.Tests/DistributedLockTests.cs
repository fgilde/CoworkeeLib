using Coworkee.Application;
using Coworkee.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Tests;

public sealed class DistributedLockTests(DatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lock_is_exclusive_per_name_until_released()
    {
        await using var provider = database.CreateServices(new TestCurrentUser(), TimeProvider.System);
        var locks = provider.GetRequiredService<IDistributedLock>();

        var first = await locks.AcquireAsync("digest", TimeSpan.Zero, Ct);
        first.ShouldNotBeNull();
        (await locks.AcquireAsync("digest", TimeSpan.FromMilliseconds(300), Ct)).ShouldBeNull();
        await using ((await locks.AcquireAsync("other", TimeSpan.Zero, Ct)).ShouldNotBeNull())
        {
        }

        var waiting = locks.AcquireAsync("digest", TimeSpan.FromSeconds(10), Ct);
        await Task.Delay(300, Ct);
        waiting.IsCompleted.ShouldBeFalse();
        await first.DisposeAsync();

        await using var second = await waiting;
        second.ShouldNotBeNull();
    }
}
