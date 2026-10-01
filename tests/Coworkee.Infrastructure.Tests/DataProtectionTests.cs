using Coworkee.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class DataProtectionTests(DatabaseFixture database) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Hosts_sharing_the_database_share_the_key_ring()
    {
        await using var api = database.CreateServices(new TestCurrentUser(), new FakeTimeProvider());
        var protectedValue = api.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("secret");

        await using var auth = database.CreateServices(new TestCurrentUser(), new FakeTimeProvider());

        auth.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedValue).ShouldBe("secret");
    }
}
